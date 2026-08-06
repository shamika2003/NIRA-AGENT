namespace SegaAgent.UI.Companion;

public readonly record struct CompanionPosition(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double CenterX =>
        Left + Width / 2.0;

    public double CenterY =>
        Top + Height / 2.0;
}