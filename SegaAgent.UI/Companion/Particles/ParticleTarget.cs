/*
 * filename: ParticleTarget.cs
 */

using System.Numerics;

namespace SegaAgent.UI.Companion.Particles;


// =============================================================
// PARTICLE ROLE
// =============================================================

public enum ParticleRole
{
    Core,

    Surface,

    Halo,

    Accent
}


// =============================================================
// PARTICLE PALETTE
//
// Providers choose the broad visual material of each particle.
//
// The renderer may still introduce small deterministic variation,
// but the form owns the intended palette assignment.
// =============================================================

public enum ParticlePalette
{
    Cyan,

    Blue,

    Violet,

    White
}


// =============================================================
// TARGET
// =============================================================

public readonly record struct ParticleTarget(
    Vector3 Position,
    float Size,
    float Brightness,
    ParticleRole Role,
    ParticlePalette Palette);
