"""Offline static packaging checks. Does NOT compile or execute C# code."""
from pathlib import Path
from xml.etree import ElementTree as ET
import json
import re

root = Path(__file__).resolve().parents[1]
json.loads((root/'global.json').read_text(encoding='utf-8'))
projects = list(root.rglob('*.csproj'))
assert len(projects) >= 6, f'Expected >=6 V2 projects, got {len(projects)}'
for project in projects:
    tree = ET.parse(project)
    for reference in tree.findall('.//ProjectReference'):
        ref = project.parent / reference.attrib['Include'].replace('\\', '/')
        assert ref.resolve().is_file(), f'Missing project reference: {project}: {ref}'
    assert not tree.findall('.//PackageReference'), f'Unexpected package dependency: {project}'
    # Phase 4E-4H approved migration adapters/tests intentionally reuse the
    # one authoritative NIRA capability, memory and state owner. These are
    # temporary references, not a second agent. Reject any NEW unrelated use.
    approved_legacy_references = {
        'Nira.V2.LegacyAdapters',
        'Nira.V2.Continuity.Tests',
        'Nira.V2.ContinuityQuality.Tests',
        'Nira.V2.Lifecycle.Tests',
        'Nira.V2.WpfBridge.Tests',
    }
    if 'NIRAAgent' in project.read_text(encoding='utf-8'):
        assert project.parent.name in approved_legacy_references, \
            f'Unreviewed legacy dependency: {project}'

for name in ('AgentLoopContracts.cs','AgentLoopEngine.cs','FileAgentStateStore.cs',
             'ResearchDocumentBuilder.cs','WorkflowEngine.cs'):
    assert any(p.name == name for p in root.rglob('*.cs')), name

sources = list(root.rglob('*.cs'))
for source in sources:
    t = source.read_text(encoding='utf-8')
    assert t.count('\0') == 0, f'Invalid NUL in {source}'
    # Catch the specific historical asynchronous-lambda compilation error.
    for m in re.finditer(r'await\s+Test\([^\n]+,\s*\(\)\s*=>', t):
        body = t[m.end(): t.find('});',m.end())]
        assert 'await ' not in body, f'Await without async lambda: {source}'

required = ['ARCHITECTURE.md','MIGRATION_AND_RETIREMENT.md','SECURITY.md','RELEASE_GATES.md']
for name in required:
    assert (root/'docs'/name).is_file(), name
assert (root/'scripts'/'Install-V2.ps1').is_file()
assert (root/'scripts'/'Verify-Foundation.ps1').is_file()
print(f'PASS static audit: {len(projects)} projects, {len(sources)} C# files, docs and reviewed migration dependencies.')
print('WARNING: static audit does not replace the Windows .NET 10 compiler or live agent evaluation.')
