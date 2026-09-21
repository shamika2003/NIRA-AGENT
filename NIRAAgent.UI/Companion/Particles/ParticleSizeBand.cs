/*
 * filename: ParticleSizeBand.cs
 */

namespace NIRAAgent.UI.Companion.Particles;

// =============================================================
// PARTICLE SIZE BAND
//
// Tiny:
//   ambient fill / soft field density.
//
// Normal:
//   structural support.
//
// Large:
//   visually important particles that can later carry the
//   strongest form/style information when custom shapes arrive.
// =============================================================

public enum ParticleSizeBand
{
    Tiny,
    Normal,
    Large
}

