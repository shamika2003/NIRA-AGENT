/*
 * filename: NIRAInteractionAppraisal.cs
 */

using NIRAAgent.Character.History;
using NIRAAgent.Character.State;

namespace NIRAAgent.Character.Appraisal;

public enum NIRAAppraisalSource
{
    Unknown,

    Semantic,

    Composite
}


public sealed record NIRAInteractionAppraisal
{
    public Guid EventId
    {
        get;
        init;
    }


    public long EventSequence
    {
        get;
        init;
    }


    public NIRASocialEventSource EventSource
    {
        get;
        init;
    }


    public NIRASocialEventKind EventKind
    {
        get;
        init;
    }


    public string EventName
    {
        get;
        init;
    } =
        string.Empty;


    public string TopicKey
    {
        get;
        init;
    } =
        string.Empty;


    public NIRASocialMeaning Meaning
    {
        get;
        init;
    } =
        NIRASocialMeaning.Neutral;


    public double Confidence
    {
        get;
        init;
    }


    public double Ambiguity
    {
        get;
        init;
    }


    public NIRAInteractionMode
        SituationMode
    {
        get;
        init;
    } =
        NIRAInteractionMode.Casual;


    public double SituationIntensity
    {
        get;
        init;
    }


    public NIRAAppraisalSource Source
    {
        get;
        init;
    }


    public NIRAInteractionAppraisal Normalize()
    {
        return this with
        {
            Meaning =
                Meaning.Normalize(),

            Confidence =
                Math.Clamp(
                    Confidence,
                    0.0,
                    1.0),

            Ambiguity =
                Math.Clamp(
                    Ambiguity,
                    0.0,
                    1.0),

            SituationIntensity =
                Math.Clamp(
                    SituationIntensity,
                    0.0,
                    1.0)
        };
    }
}
