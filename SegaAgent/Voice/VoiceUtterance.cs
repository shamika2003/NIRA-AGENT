/*
 * filename: VoiceUtterance.cs
 */

namespace SegaAgent.Voice;

public sealed record VoiceUtterance
{
    public Guid ResponseId
    {
        get;
    }


    public int Sequence
    {
        get;
    }


    public string Text
    {
        get;
    }


    public SegaVoiceExpression Expression
    {
        get;
    }


    public VoiceUtterance(
        Guid responseId,
        int sequence,
        string text,
        SegaVoiceExpression expression)
    {
        if (responseId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Voice response id cannot be empty.",
                nameof(responseId));
        }


        if (sequence <=
            0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence));
        }


        ArgumentException
            .ThrowIfNullOrWhiteSpace(
                text);


        ResponseId =
            responseId;


        Sequence =
            sequence;


        Text =
            text.Trim();


        Expression =
            expression.Normalize();
    }
}