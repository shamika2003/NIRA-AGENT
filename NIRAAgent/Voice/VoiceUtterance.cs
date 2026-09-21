/*
 * filename: VoiceUtterance.cs
 */

namespace NIRAAgent.Voice;

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


    public NIRAVoiceExpression Expression
    {
        get;
    }


    public VoiceUtterance(
        Guid responseId,
        int sequence,
        string text,
        NIRAVoiceExpression expression)
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
