/*
 * filename: SegaInteractionAppraisal.cs
 */

using SegaAgent.Character.History;
using SegaAgent.Character.State;

namespace SegaAgent.Character.Appraisal;

public enum SegaAppraisalSource
{
    Unknown,

    Semantic,

    Composite
}


public sealed record SegaInteractionAppraisal
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


    public SegaSocialEventSource EventSource
    {
        get;
        init;
    }


    public SegaSocialEventKind EventKind
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


    public SegaSocialMeaning Meaning
    {
        get;
        init;
    } =
        SegaSocialMeaning.Neutral;


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


    public SegaInteractionMode
        SituationMode
    {
        get;
        init;
    } =
        SegaInteractionMode.Casual;


    public double SituationIntensity
    {
        get;
        init;
    }


    public SegaAppraisalSource Source
    {
        get;
        init;
    }


    public SegaInteractionAppraisal Normalize()
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