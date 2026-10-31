using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Diagnostics;

namespace NIRAAgent.Presentation;

/// <summary>
/// Reads model-authored, DECLARATIVE presentation data. Accepts equivalent
/// object/array envelopes, but never invents a card, chart value, or source fact.
/// The renderer, not the model, owns all UI markup and interaction.
/// </summary>
public static class NIRARichBlockJsonReader
{
    private static readonly string[] RootNames =
        { "displayBlocks", "screenBlocks", "richBlocks", "contentBlocks", "blocks" };
    private static readonly string[] ContainerNames =
        { "blocks", "displayBlocks", "cards", "items", "entries" };
    private static readonly string[] TitleNames = { "title", "heading", "name", "label" };
    private static readonly string[] BodyNames =
        { "text", "body", "content", "description", "summary" };

    public static IReadOnlyList<NIRARichBlock> Read(JsonElement root)
    {
        // A size/complexity boundary, NOT a semantic limit of eight or any other
        // fixed number of cards. Never silently truncate an otherwise valid list.
        if (root.GetRawText().Length > NIRAPresentationPolicy.MaxPresentationPayloadCharacters)
        {
            NIRADiagnosticTrace.Event("RICH-PRESENTATION-PARSE", "Reason=PayloadTooLarge");
            return Array.Empty<NIRARichBlock>();
        }
        var result = new List<NIRARichBlock>();
        JsonElement envelope = default;
        string source = "none";
        foreach (string name in RootNames)
            if (Property(root, name, out var candidate))
            {
                // Prefer the canonical key, but don't let an empty canonical
                // array hide actual blocks supplied under another known key.
                if (source == "none") { source = name; envelope = candidate; }
                if (candidate.ValueKind == JsonValueKind.Array && candidate.GetArrayLength() == 0)
                    continue;
                if (candidate.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue;
                source = name; envelope = candidate; break;
            }

        if (source == "none" || envelope.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
            (envelope.ValueKind == JsonValueKind.Array && envelope.GetArrayLength() == 0))
        {
            // A model can place its screen payload under a presentation object.
            // Only descend when that object actually contains typed display data.
            foreach (string name in new[] { "screen", "presentation", "ui" })
                if (Property(root, name, out var candidate) &&
                    candidate.ValueKind == JsonValueKind.Object)
                {
                    foreach (string nested in RootNames.Concat(new[] { "cards", "items" }))
                        if (Property(candidate, nested, out envelope))
                        { source = name + "." + nested; break; }
                    if (source != "none") break;
                }
        }

        // Distinguish the LLM omitting blocks from a parser/rendering failure.
        // Diagnostics include ONLY shape/count metadata, never model content.
        if (source == "none")
        {
            Debug.WriteLine("[RichPresentation] Source=none | Raw=none | Accepted=0 | Reason=NotProvided");
            // Metadata only: the event log must distinguish model omission from
            // malformed typed data without capturing any user/model text.
            NIRADiagnosticTrace.Event("RICH-PRESENTATION-PARSE",
                "Source=none Raw=0 Accepted=0 Reason=NotProvided");
            return Array.Empty<NIRARichBlock>();
        }

        int raw = envelope.ValueKind == JsonValueKind.Array
            ? envelope.GetArrayLength() : envelope.ValueKind == JsonValueKind.Null ? 0 : 1;
        ReadBlocks(envelope, result, 0, source.EndsWith(".cards", StringComparison.OrdinalIgnoreCase) ? "card" : "text");
        // The model can provide valid facts in a generic table/card while ignoring
        // a more specific typed screen component. Adapt only unambiguous
        // semantic schemas; never synthesize tasks, dates, or completion values.
        var shaped = result.Select(AdaptSemanticShape).ToArray();
        int adapted = shaped.Where((b, i) => b.Type != result[i].Type).Count();
        IReadOnlyList<NIRARichBlock> accepted = NIRAPresentationPolicy.Normalize(shaped);
        Debug.WriteLine($"[RichPresentation] Source={source} | Raw={raw} | Read={result.Count} | Adapted={adapted} | Accepted={accepted.Count} | Types={string.Join(",", accepted.Select(b => b.Type))}" +
            (accepted.Count == 0 && raw > 0 ? " | Reason=UnsupportedOrIncompleteShape" : ""));
        NIRADiagnosticTrace.Event("RICH-PRESENTATION-PARSE",
            $"Source={source} Raw={raw} Read={result.Count} " +
            $"Accepted={accepted.Count} " +
            $"Types={string.Join(",", accepted.Select(b => b.Type))} " +
            $"Reason={(accepted.Count == 0 && raw > 0 ? "UnsupportedOrIncompleteShape" : "AcceptedOrEmpty")}");
        if (accepted.Count == 0 && result.Count > 0)
            Debug.WriteLine("[RichPresentation] REJECTED_SHAPES | " +
                string.Join(";", shaped.Select(b =>
                    $"{b.Type}:title={(!string.IsNullOrWhiteSpace(b.Title) ? 1 : 0)}," +
                    $"text={(!string.IsNullOrWhiteSpace(b.Text) ? 1 : 0)}," +
                    $"items={b.Items.Count},panels={b.Panels.Count}")));
        return accepted;
    }

    private static void ReadBlocks(JsonElement element, List<NIRARichBlock> results,
        int depth, string defaultType)
    {
        if (depth > 7) return;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                ReadBlocks(child, results, depth + 1, defaultType);
                // Continue until the structured payload is fully read.
            }
            return;
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            string raw = (element.GetString() ?? "").Trim();
            if (raw.Length == 0 || raw.Length > 12000) return;
            if (raw.StartsWith('{') || raw.StartsWith('['))
            {
                try
                {
                    using var nested = JsonDocument.Parse(raw);
                    ReadBlocks(nested.RootElement, results, depth + 1, defaultType);
                    return;
                }
                catch (JsonException) { /* Keep ordinary strings as text. */ }
            }
            int colon = raw.IndexOf(':');
            results.Add(defaultType == "card" && colon is > 0 and < 45
                ? new NIRARichBlock { Type = "card", Title = raw[..colon].Trim(),
                    Text = raw[(colon + 1)..].Trim() }
                : new NIRARichBlock { Type = defaultType, Text = raw });
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;

        string kind = First(element, "", "type", "kind", "blockType")
            .Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");
        bool group = kind is "cards" or "cardgroup" or "comparison" or "grid" or "cardlist";
        foreach (string key in ContainerNames)
            if (Property(element, key, out var nested) &&
                (nested.ValueKind == JsonValueKind.Array || nested.ValueKind == JsonValueKind.Object) &&
                (group || key is "cards" or "blocks" or "displayBlocks"))
            {
                if (nested.ValueKind == JsonValueKind.Object && key == "cards")
                {
                    // {"cards":{"Option A":{"description":"..."}, ...}}
                    foreach (var named in nested.EnumerateObject())
                    {
                        // Continue until the structured payload is fully read.
                        if (named.Value.ValueKind == JsonValueKind.Object)
                        {
                            var card = Block(named.Value, "card", named.Name);
                            if (card != null) results.Add(card);
                        }
                        else results.Add(new NIRARichBlock { Type = "card",
                            Title = named.Name, Text = AsText(named.Value) });
                    }
                }
                else ReadBlocks(nested, results, depth + 1,
                    key is "cards" or "items" or "entries" ? "card" : defaultType);
                return;
            }

        // An array of cards can be placed under a single {type:"card",items:[...]}.
        if (kind == "card" && Property(element, "items", out var cardItems) &&
            cardItems.ValueKind == JsonValueKind.Array)
        {
            ReadBlocks(cardItems, results, depth + 1, "card");
            return;
        }

        var block = Block(element, defaultType, "");
        if (block != null) results.Add(block);
    }

    // Structural compatibility for typed UI. This operates on the data the model
    // actually returned, not a second LLM call or a guess based on a user phrase.
    // Important: a "date" cell in a generated table has no provenance. A timeline
    // uses its ordinal day/stage and task here, never an unverified date cell.
    private static NIRARichBlock AdaptSemanticShape(NIRARichBlock block)
    {
        if (block.Type == "table" && block.Rows.Count > 0)
        {
            int FindColumn(params string[] names) =>
                Enumerable.Range(0, block.Columns.Count).FirstOrDefault(
                    i => names.Any(name => string.Equals(block.Columns[i].Trim().TrimEnd('?', ':'),
                        name, StringComparison.OrdinalIgnoreCase)), -1);
            int eventColumn = FindColumn("task", "event", "activity", "milestone", "action", "step");
            int whenColumn = FindColumn("day", "phase", "stage", "when", "time", "date");
            bool timeline = Regex.IsMatch(block.Title, @"\b(timeline|milestones|chronology)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (timeline && eventColumn >= 0 && whenColumn >= 0 && eventColumn != whenColumn)
            {
                var events = block.Rows
                    .Where(row => row.Count > Math.Max(eventColumn, whenColumn))
                    .Select(row => row[whenColumn].Trim() + " — " + row[eventColumn].Trim())
                    .Where(value => value.Length > 3).ToArray();
                if (events.Length > 0)
                    return block with { Type = "timeline", Items = events };
            }

            int taskColumn = FindColumn("task", "item", "action", "step", "to do", "todo");
            int doneColumn = FindColumn("done", "complete", "completed", "checked", "status");
            bool checklist = Regex.IsMatch(block.Title, @"\b(checklist|to.do list)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (taskColumn >= 0 && (checklist || doneColumn >= 0))
            {
                var tasks = block.Rows.Where(row => row.Count > taskColumn)
                    .Select(row => Regex.Replace(row[taskColumn].Trim(), @"^\s*(?:\[.\]|☐|☑)\s*", ""))
                    .Where(item => item.Length > 0).ToArray();
                if (tasks.Length > 0)
                    return block with { Type = "checklist", Items = tasks };
            }
        }
        if (block.Type == "card" &&
            Regex.IsMatch(block.Title, @"\b(progress|completion)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            double? value = block.ProgressValue, maximum = block.ProgressMaximum;
            if (!value.HasValue || !maximum.HasValue)
            {
                // Only an explicitly supplied count ratio is valid evidence.
                // The calculated percentage comes from the WPF renderer, not
                // from the model's possibly rounded percentage in the card.
                Match ratio = Regex.Match(block.Text,
                    @"(?<!\d)(?<done>\d+(?:\.\d+)?)\s*/\s*(?<total>\d+(?:\.\d+)?)(?!\d)",
                    RegexOptions.CultureInvariant);
                if (ratio.Success &&
                    double.TryParse(ratio.Groups["done"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double completed) &&
                    double.TryParse(ratio.Groups["total"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double total))
                { value = completed; maximum = total; }
            }
            if (value is double v && maximum is double max &&
                double.IsFinite(v) && double.IsFinite(max) && max > 0 && v >= 0 && v <= max)
            {
                string unit = block.Unit;
                if (unit.Length == 0)
                {
                    Match units = Regex.Match(block.Text,
                        @"\b(chapters?|pages?|hours?|minutes?|tasks?|items?|lessons?|days?|steps?)\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (units.Success) unit = units.Value.ToLowerInvariant();
                }
                string remainder = (max - v).ToString("0.##", CultureInfo.InvariantCulture);
                return block with { Type = "progress", ProgressValue = v,
                    ProgressMaximum = max, Unit = unit,
                    Text = remainder + (unit.Length > 0 ? " " + unit : "") + " remaining" };
            }
        }
        return block;
    }

    private static NIRARichBlock? Block(JsonElement element, string defaultType, string titleFallback)
    {
        string type = First(element, defaultType, "type", "kind", "blockType")
            .Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");
        type = type switch
        {
            "paragraph" or "markdown" => "text",
            "snippet" or "codeblock" => "code",
            "accordion" or "collapsible" or "expandable" => "details",
            "bullets" or "bulletlist" or "steps" => "list",
            "tasklist" or "checkboxes" => "checklist",
            "milestones" or "history" => "timeline",
            "progressbar" or "completion" => "progress",
            "tabbed" or "tabgroup" => "tabs",
            "choices" or "suggestions" or "followup" or "quickreplies" => "followups",
            "blockquote" => "quote",
            "line" or "linechart" or "bar" or "barchart" => "chart",
            "comparison" or "cards" or "cardgroup" => "card",
            "feature" or "featured" or "spotlight" or "hero" => "hero",
            "takeaway" or "keyinsight" or "highlight" or "insight" => "insight",
            "resource" or "reference" or "sourcelink" or "source" => "source",
            "videolink" or "videoreference" or "video" => "video",
            "stat" or "statcard" or "statistic" or "kpi" or "metriccard" or "metricpanel" => "metric",
            "math" or "latex" or "formula" or "mathequation" => "equation",
            _ => type
        };
        // "file" cards are reserved for authoritative runtime tool evidence.
        // A model's unverified success claim must never impersonate that status UI.
        if (type == "file") return null;
        if (type is not ("heading" or "text" or "hero" or "insight" or "source" or "video" or "card" or "metric" or "equation" or "table" or "chart" or "code" or "details" or "list" or "quote" or "timeline" or "checklist" or "progress" or "tabs" or "followups"))
            type = defaultType;

        // A model may use objects instead of the contracted string arrays.
        // Extract their human-readable fields; never print JSON property names
        // such as "text:", "title:" or "checked: false" on screen.
        List<string> items = PresentationItems(element, "items", type);
        if (items.Count == 0) items = PresentationItems(element, "bullets", type);
        if (items.Count == 0 && type == "timeline") items = PresentationItems(element, "events", type);
        if (items.Count == 0 && type == "checklist") items = PresentationItems(element, "tasks", type);
        if (items.Count == 0 && type == "tabs") items = PresentationItems(element, "labels", type);
        if (items.Count == 0 && type == "followups") items = PresentationItems(element, "options", type);
        if (items.Count == 0 && type == "followups") items = PresentationItems(element, "suggestions", type);
        var panels = type == "tabs" ? TabPanelStrings(element, "panels") : Strings(element, "panels");
        if (panels.Count == 0 && type == "tabs") panels = TabPanelStrings(element, "contents");
        // Also accept tabs authored as items:[{label,content}, ...].
        if (type == "tabs" && panels.Count == 0 &&
            Property(element, "items", out var tabItems) && tabItems.ValueKind == JsonValueKind.Array)
        {
            var objects = tabItems.EnumerateArray().ToArray();
            if (objects.Length >= 2 && objects.All(e => e.ValueKind == JsonValueKind.Object))
            {
                var labelsFromItems = objects.Select(e => First(e, "", "label", "title", "name")).ToList();
                var contentFromItems = objects.Select(PanelContent).ToList();
                if (labelsFromItems.All(x => x.Length > 0) && contentFromItems.All(x => x.Length > 0))
                { items = labelsFromItems; panels = contentFromItems; }
            }
        }
        List<string> cols = Strings(element, "columns");
        if (cols.Count == 0) cols = Strings(element, "headers");
        var rows = new List<IReadOnlyList<string>>();
        if (Property(element, "rows", out var rowArray) && rowArray.ValueKind == JsonValueKind.Array)
            foreach (var row in rowArray.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Array)
                    rows.Add(row.EnumerateArray().Select(AsText).ToArray());
                else if (row.ValueKind == JsonValueKind.Object && cols.Count > 0)
                    rows.Add(cols.Select(c => Property(row, c, out var cell) ? AsText(cell) : "").ToArray());
            }
        var labels = Strings(element, "labels");
        var values = new List<double>();
        if (Property(element, "values", out var valArray) && valArray.ValueKind == JsonValueKind.Array)
            foreach (var value in valArray.EnumerateArray())
                if (ParseNumber(value, out var number)) values.Add(number);
        if ((labels.Count == 0 || values.Count == 0) &&
            Property(element, "data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            labels = new List<string>(); values.Clear();
            foreach (var point in data.EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Object ||
                    !Property(point, "value", out var value) ||
                    !ParseNumber(value, out var number)) continue;
                string label = First(point, "", "label", "name");
                if (label.Length == 0) continue;
                labels.Add(label); values.Add(number);
            }
        }

        string body = First(element, "", BodyNames);
        if (type == "metric")
        {
            // Metric schemas commonly use {label,value,unit}, not {title,text,unit}.
            // Preserve the model-supplied value instead of dropping an otherwise
            // valid metric as an empty text block. No value is inferred here.
            string explicitValue = First(element, "", "value", "metricValue", "amount", "number");
            if (explicitValue.Length > 0) body = explicitValue;
        }
        if (type == "code" && body.Length == 0) body = First(element, "", "code");
        if (type == "equation" && body.Length == 0)
            body = First(element, "", "latex", "formula", "expression");
        if (type == "equation") body = NIRAMathMarkup.NormalizeEquationInput(body);
        // A source card may use a conventional URL field. This only preserves
        // model-supplied link text; it does not fetch or verify the destination.
        if ((type is "source" or "video") && body.Length == 0)
            body = First(element, "", "url", "href", "uri");
        if (type == "card")
        {
            var details = new List<string>();
            foreach (var p in element.EnumerateObject())
            {
                if (p.NameEquals("type") || p.NameEquals("kind") ||
                    p.NameEquals("blockType") ||
                    TitleNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase) ||
                    BodyNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) continue;
                if (p.Name is "items" or "cards" or "blocks" or "data" or "unit" or "tone" or "accent") continue;
                string value = AsText(p.Value);
                if (value.Length > 0) details.Add(p.Name + ": " + value);
            }
            if (details.Count > 0)
                body = string.Join("\n", new[] { body }.Where(s => s.Length > 0).Concat(details));
        }
        return new NIRARichBlock
        {
            Type = type,
            Title = First(element, titleFallback, TitleNames),
            Text = body,
            Language = First(element, "", "language"),
            Unit = First(element, "", "unit"),
            Items = items,
            Panels = panels,
            ImageSubject = First(element, "", "imageSubject"),
            Columns = cols,
            Rows = rows,
            Labels = labels,
            Values = values,
            ProgressValue = Number(element, "value"),
            ProgressMaximum = Number(element, "maximum") ?? Number(element, "total")
        };
    }

    // Presentation-only shape compatibility. These routines preserve supplied
    // words and dates; they do not infer task completion or invent milestones.
    private static List<string> PresentationItems(JsonElement obj, string key, string type)
    {
        if (!Property(obj, key, out var values) || values.ValueKind != JsonValueKind.Array)
            return new List<string>();
        var items = new List<string>();
        foreach (var value in values.EnumerateArray())
        {
            string text;
            if (value.ValueKind != JsonValueKind.Object)
                text = AsText(value);
            else if (type == "timeline")
            {
                string when = First(value, "", "date", "year", "period", "when", "day", "stage", "time");
                string title = First(value, "", "title", "event", "milestone", "label", "name");
                string description = First(value, "", "text", "description", "body", "content", "summary");
                if (title.Length == 0) title = description;
                else if (description.Length > 0 && !string.Equals(title, description, StringComparison.Ordinal))
                    title += "\n" + description;
                text = when.Length > 0 && title.Length > 0 ? when + " — " + title : title;
            }
            else if (type == "checklist")
            {
                text = First(value, "", "text", "task", "label", "title", "description", "name");
                string detail = First(value, "", "details", "note");
                if (text.Length > 0 && detail.Length > 0) text += "\n" + detail;
                // A model's checked/done flag is NEVER proof that work was done.
            }
            else
                text = First(value, "", "label", "title", "name", "text", "description");
            if (!string.IsNullOrWhiteSpace(text)) items.Add(text.Trim());
        }
        return items;
    }

    private static List<string> TabPanelStrings(JsonElement obj, string key)
    {
        if (!Property(obj, key, out var values) || values.ValueKind != JsonValueKind.Array)
            return new List<string>();
        return values.EnumerateArray().Select(PanelContent).ToList();
    }

    // Return meaningful Markdown, not an object's property dump. Markdown is
    // interpreted only by NIRA's safe local WPF renderer, never as executable UI.
    private static string PanelContent(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        if (value.ValueKind == JsonValueKind.Array)
            return string.Join("\n", value.EnumerateArray().Select(PanelContent)
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (value.ValueKind != JsonValueKind.Object) return AsText(value);
        var parts = new List<string>();
        string title = First(value, "", "heading", "title");
        if (title.Length > 0) parts.Add("### " + title);
        string body = First(value, "", "text", "body", "content", "description", "summary", "overview");
        if (body.Length > 0) parts.Add(body);
        if (Property(value, "sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
            foreach (var section in sections.EnumerateArray().Take(12))
            {
                string sectionText = PanelContent(section);
                if (sectionText.Length > 0) parts.Add(sectionText);
            }
        foreach (var key in new[] { "bullets", "steps", "points" })
            if (Property(value, key, out var bullets) && bullets.ValueKind == JsonValueKind.Array)
            {
                var lines = bullets.EnumerateArray().Take(24).Select(e =>
                    e.ValueKind == JsonValueKind.Object
                        ? First(e, "", "text", "label", "title", "description") : AsText(e));
                parts.Add(string.Join("\n", lines.Where(x => x.Length > 0).Select(x => "- " + x)));
            }
        return string.Join("\n\n", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static List<string> Strings(JsonElement obj, string key) =>
        Property(obj, key, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(AsText).ToList()
            : new List<string>();

    private static string First(JsonElement obj, string fallback, params string[] keys)
    {
        foreach (string key in keys)
            if (Property(obj, key, out var value))
            {
                string text = AsText(value);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        return fallback;
    }

    private static string AsText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(AsText)),
        JsonValueKind.Object => string.Join("; ", value.EnumerateObject()
            .Select(p => p.Name + ": " + AsText(p.Value))),
        _ => ""
    };

    private static double? Number(JsonElement element, string key) =>
        Property(element, key, out var candidate) && ParseNumber(candidate, out var number)
            ? number : null;

    private static bool ParseNumber(JsonElement value, out double number)
    {
        number = 0;
        bool valid = value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDouble(out number),
            JsonValueKind.String => double.TryParse(value.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out number),
            _ => false
        };
        return valid && double.IsFinite(number);
    }

    private static bool Property(JsonElement element, string key, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(key, out value)) return true;
            foreach (var p in element.EnumerateObject())
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                { value = p.Value; return true; }
        }
        value = default;
        return false;
    }
}
