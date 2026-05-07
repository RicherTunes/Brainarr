using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for CacheSettings.
    /// Source: Brainarr.Plugin/Configuration/CacheSettings.cs (60 lines)
    /// Tests cover: constants, defaults, Normalize, WithCapacity, WithTtl, ClampTtl, record immutability.
    /// </summary>
    public class CacheSettingsCovTests
    {
        #region Constants (lines 7-13)

        // Proof: grep -n "const" Brainarr.Plugin/Configuration/CacheSettings.cs
        //   7:    public const int MinCapacity = 16;
        //   8:    public const int MaxCapacity = 1024;
        //   9:    public const int DefaultCapacity = 256;
        //   10:    public const int MinTtlMinutes = 1;
        //   11:    public const int MaxTtlMinutes = 60;
        //   12:    public const int DefaultTtlMinutes = 5;
        [Fact]
        public void Constants_HaveExpectedValues()
        {
            // Proof: lines 7-12 — all 6 const declarations
            CacheSettings.MinCapacity.Should().Be(16, "because MinCapacity is 16 (line 7)");
            CacheSettings.MaxCapacity.Should().Be(1024, "because MaxCapacity is 1024 (line 8)");
            CacheSettings.DefaultCapacity.Should().Be(256, "because DefaultCapacity is 256 (line 9)");
            CacheSettings.MinTtlMinutes.Should().Be(1, "because MinTtlMinutes is 1 (line 10)");
            CacheSettings.MaxTtlMinutes.Should().Be(60, "because MaxTtlMinutes is 60 (line 11)");
            CacheSettings.DefaultTtlMinutes.Should().Be(5, "because DefaultTtlMinutes is 5 (line 12)");
        }

        #endregion

        #region Default Instance and Property Defaults (lines 15-19)

        // Proof: grep -n "Default\|PlanCacheCapacity\|PlanCacheTtl" Brainarr.Plugin/Configuration/CacheSettings.cs
        //   15:    public int PlanCacheCapacity { get; init; } = DefaultCapacity;
        //   17:    public TimeSpan PlanCacheTtl { get; init; } = TimeSpan.FromMinutes(DefaultTtlMinutes);
        //   19:    public static CacheSettings Default { get; } = new CacheSettings();
        [Fact]
        public void Default_ReturnsInstanceWithDefaultValues()
        {
            // Proof: line 19 — static Default property
            var settings = CacheSettings.Default;

            settings.PlanCacheCapacity.Should().Be(256, "because DefaultCapacity is 256 (line 15)");
            settings.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(5), "because DefaultTtlMinutes is 5 (line 17)");
        }

        [Fact]
        public void Constructor_Defaults_ProduceDefaultValues()
        {
            // Proof: lines 15, 17 — property initializers use DefaultCapacity and DefaultTtlMinutes
            var settings = new CacheSettings();

            settings.PlanCacheCapacity.Should().Be(256, "because PlanCacheCapacity defaults to 256 (line 15)");
            settings.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(5), "because PlanCacheTtl defaults to 5 min (line 17)");
        }

        #endregion

        #region Normalize (lines 21-30)

        // Proof: grep -n "Normalize" Brainarr.Plugin/Configuration/CacheSettings.cs
        //   21:    public CacheSettings Normalize()
        [Fact]
        public void Normalize_WithDefaults_ReturnsEquivalentSettings()
        {
            // Proof: line 21-30 — Normalize clamps capacity and TTL but defaults are already in range
            var settings = new CacheSettings();
            var result = settings.Normalize();

            result.PlanCacheCapacity.Should().Be(256, "because default 256 is within [16, 1024]");
            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(5), "because default 5 min is within (0, 60]");
        }

        [Fact]
        public void Normalize_WithCapacityBelowMin_ClampsToMinCapacity()
        {
            // Proof: line 23 — Math.Clamp(PlanCacheCapacity, MinCapacity, MaxCapacity)
            var settings = new CacheSettings { PlanCacheCapacity = 0 };
            var result = settings.Normalize();

            result.PlanCacheCapacity.Should().Be(16, "because capacity 0 is clamped to MinCapacity 16 (line 23)");
        }

        [Fact]
        public void Normalize_WithCapacityAboveMax_ClampsToMaxCapacity()
        {
            // Proof: line 23 — Math.Clamp(PlanCacheCapacity, MinCapacity, MaxCapacity)
            var settings = new CacheSettings { PlanCacheCapacity = 9999 };
            var result = settings.Normalize();

            result.PlanCacheCapacity.Should().Be(1024, "because capacity 9999 is clamped to MaxCapacity 1024 (line 23)");
        }

        [Fact]
        public void Normalize_WithNegativeCapacity_ClampsToMinCapacity()
        {
            // Proof: line 23 — Math.Clamp clamps negative values to MinCapacity
            var settings = new CacheSettings { PlanCacheCapacity = -100 };
            var result = settings.Normalize();

            result.PlanCacheCapacity.Should().Be(16, "because negative capacity is clamped to MinCapacity 16 (line 23)");
        }

        [Fact]
        public void Normalize_WithZeroTtl_UsesDefaultTtl()
        {
            // Proof: line 47-50 — if (value <= TimeSpan.Zero) return TimeSpan.FromMinutes(DefaultTtlMinutes)
            var settings = new CacheSettings { PlanCacheTtl = TimeSpan.Zero };
            var result = settings.Normalize();

            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(5),
                "because zero TTL falls through to default (line 48-50)");
        }

        [Fact]
        public void Normalize_WithNegativeTtl_UsesDefaultTtl()
        {
            // Proof: line 47-50 — if (value <= TimeSpan.Zero) return TimeSpan.FromMinutes(DefaultTtlMinutes)
            var settings = new CacheSettings { PlanCacheTtl = TimeSpan.FromMinutes(-10) };
            var result = settings.Normalize();

            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(5),
                "because negative TTL falls through to default (line 48-50)");
        }

        [Fact]
        public void Normalize_WithTtlAboveMax_ClampsToMaxTtl()
        {
            // Proof: line 52-56 — if (value > max) return max; where max = TimeSpan.FromMinutes(MaxTtlMinutes)
            var settings = new CacheSettings { PlanCacheTtl = TimeSpan.FromMinutes(120) };
            var result = settings.Normalize();

            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(60),
                "because 120 min TTL is clamped to MaxTtlMinutes 60 (line 53-56)");
        }

        [Fact]
        public void Normalize_WithBothOutOfBounds_ClampsBoth()
        {
            // Proof: lines 23-24 — both capacity and TTL are clamped
            var settings = new CacheSettings
            {
                PlanCacheCapacity = 5,
                PlanCacheTtl = TimeSpan.FromDays(1)
            };
            var result = settings.Normalize();

            result.PlanCacheCapacity.Should().Be(16, "because capacity 5 is clamped to MinCapacity 16 (line 23)");
            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(60),
                "because 1 day TTL is clamped to MaxTtlMinutes 60 (line 53-56)");
        }

        [Fact]
        public void Normalize_DoesNotMutateOriginal()
        {
            // Proof: line 25 — `this with { ... }` creates a new record
            var settings = new CacheSettings { PlanCacheCapacity = 0 };
            var result = settings.Normalize();

            settings.PlanCacheCapacity.Should().Be(0, "because Normalize returns a new record via 'with' (line 25)");
            result.PlanCacheCapacity.Should().Be(16, "because the new record is clamped (line 23)");
        }

        #endregion

        #region WithCapacity (lines 32-38)

        // Proof: grep -n "WithCapacity" Brainarr.Plugin/Configuration/CacheSettings.cs
        //   32:    public CacheSettings WithCapacity(int capacity)
        //   36:            PlanCacheCapacity = Math.Clamp(capacity, MinCapacity, MaxCapacity)
        [Fact]
        public void WithCapacity_BelowMin_ClampsToMin()
        {
            // Proof: line 36 — Math.Clamp(capacity, MinCapacity, MaxCapacity)
            var settings = new CacheSettings();
            var result = settings.WithCapacity(5);

            result.PlanCacheCapacity.Should().Be(16, "because capacity 5 is clamped to MinCapacity 16 (line 36)");
        }

        [Fact]
        public void WithCapacity_AboveMax_ClampsToMax()
        {
            // Proof: line 36 — Math.Clamp(capacity, MinCapacity, MaxCapacity)
            var settings = new CacheSettings();
            var result = settings.WithCapacity(5000);

            result.PlanCacheCapacity.Should().Be(1024, "because capacity 5000 is clamped to MaxCapacity 1024 (line 36)");
        }

        [Fact]
        public void WithCapacity_InRange_UsesExactValue()
        {
            // Proof: line 36 — Math.Clamp returns exact value when in range
            var settings = new CacheSettings();
            var result = settings.WithCapacity(128);

            result.PlanCacheCapacity.Should().Be(128, "because 128 is within [16, 1024] (line 36)");
        }

        [Fact]
        public void WithCapacity_AtExactBoundaries_ReturnsBoundaryValues()
        {
            // Proof: line 36 — Math.Clamp includes boundary values
            var settings = new CacheSettings();

            var minResult = settings.WithCapacity(CacheSettings.MinCapacity);
            minResult.PlanCacheCapacity.Should().Be(16, "because MinCapacity is a valid boundary (line 36)");

            var maxResult = settings.WithCapacity(CacheSettings.MaxCapacity);
            maxResult.PlanCacheCapacity.Should().Be(1024, "because MaxCapacity is a valid boundary (line 36)");
        }

        [Fact]
        public void WithCapacity_DoesNotMutateOriginal()
        {
            // Proof: line 34 — `this with { ... }` creates a new record
            var settings = new CacheSettings();
            var result = settings.WithCapacity(512);

            settings.PlanCacheCapacity.Should().Be(256, "because original is unchanged (line 34)");
            result.PlanCacheCapacity.Should().Be(512, "because new record has updated capacity (line 36)");
        }

        #endregion

        #region WithTtl (lines 40-43)

        // Proof: grep -n "WithTtl" Brainarr.Plugin/Configuration/CacheSettings.cs
        //   40:    public CacheSettings WithTtl(TimeSpan ttl)
        //   42:        return this with { PlanCacheTtl = ClampTtl(ttl) };
        [Fact]
        public void WithTtl_Zero_ReturnsDefaultTtl()
        {
            // Proof: line 47-50 — ClampTtl returns default for <= TimeSpan.Zero
            var settings = new CacheSettings();
            var result = settings.WithTtl(TimeSpan.Zero);

            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(5),
                "because zero TTL is replaced by default 5 min (line 48-50)");
        }

        [Fact]
        public void WithTtl_Negative_ReturnsDefaultTtl()
        {
            // Proof: line 47-50 — ClampTtl returns default for <= TimeSpan.Zero
            var settings = new CacheSettings();
            var result = settings.WithTtl(TimeSpan.FromSeconds(-30));

            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(5),
                "because negative TTL is replaced by default 5 min (line 48-50)");
        }

        [Fact]
        public void WithTtl_AboveMax_ClampsToMax()
        {
            // Proof: line 52-56 — ClampTtl clamps values > max to max
            var settings = new CacheSettings();
            var result = settings.WithTtl(TimeSpan.FromHours(2));

            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(60),
                "because 2 hours is clamped to MaxTtlMinutes 60 (line 53-56)");
        }

        [Fact]
        public void WithTtl_InRange_UsesExactValue()
        {
            // Proof: line 58 — ClampTtl returns value unchanged when in range
            var settings = new CacheSettings();
            var result = settings.WithTtl(TimeSpan.FromMinutes(30));

            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(30),
                "because 30 min is within (0, 60] range (line 58)");
        }

        [Fact]
        public void WithTtl_AtMaxBoundary_ReturnsMaxValue()
        {
            // Proof: line 53 — if (value > max) uses strict greater-than, so max is valid
            var settings = new CacheSettings();
            var result = settings.WithTtl(TimeSpan.FromMinutes(60));

            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(60),
                "because exactly 60 min is not > max, so it passes through (line 53)");
        }

        [Fact]
        public void WithTtl_DoesNotMutateOriginal()
        {
            // Proof: line 42 — `this with { ... }` creates a new record
            var settings = new CacheSettings();
            var result = settings.WithTtl(TimeSpan.FromMinutes(30));

            settings.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(5),
                "because original TTL is unchanged (line 42)");
            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(30),
                "because new record has updated TTL (line 42)");
        }

        #endregion

        #region ClampTtl via WithTtl — Min Boundary (lines 45-58)

        // Proof: grep -n "ClampTtl" Brainarr.Plugin/Configuration/CacheSettings.cs
        //   25:        var ttl = ClampTtl(PlanCacheTtl);
        //   42:        return this with { PlanCacheTtl = ClampTtl(ttl) };
        //   45:    private static TimeSpan ClampTtl(TimeSpan value)
        [Fact]
        public void WithTtl_JustAboveZero_ReturnsExactValue()
        {
            // Proof: line 47 — if (value <= TimeSpan.Zero) is strict, so tick above zero passes
            var settings = new CacheSettings();
            var result = settings.WithTtl(TimeSpan.FromTicks(1));

            result.PlanCacheTtl.Should().Be(TimeSpan.FromTicks(1),
                "because 1 tick is > TimeSpan.Zero so it passes through (line 47)");
        }

        [Fact]
        public void WithTtl_OneMinute_ReturnsExactValue()
        {
            // Proof: line 58 — return value when in range; MinTtlMinutes=1 is in range
            var settings = new CacheSettings();
            var result = settings.WithTtl(TimeSpan.FromMinutes(1));

            result.PlanCacheTtl.Should().Be(TimeSpan.FromMinutes(1),
                "because 1 minute is within (0, 60] (line 58)");
        }

        #endregion

        #region Record Behavior

        [Fact]
        public void Normalize_ReturnsNewRecordNotSameReference()
        {
            // Proof: line 25 — `this with { ... }` always creates a new instance
            var settings = new CacheSettings();
            var result = settings.Normalize();

            result.Should().NotBeSameAs(settings,
                "because Normalize uses 'with' expression which creates a new record instance (line 25)");
        }

        [Fact]
        public void WithCapacity_ReturnsNewRecordNotSameReference()
        {
            // Proof: line 34 — `this with { ... }` always creates a new instance
            var settings = new CacheSettings();
            var result = settings.WithCapacity(256);

            result.Should().NotBeSameAs(settings,
                "because WithCapacity uses 'with' expression which creates a new record instance (line 34)");
        }

        [Fact]
        public void WithTtl_ReturnsNewRecordNotSameReference()
        {
            // Proof: line 42 — `this with { ... }` always creates a new instance
            var settings = new CacheSettings();
            var result = settings.WithTtl(TimeSpan.FromMinutes(5));

            result.Should().NotBeSameAs(settings,
                "because WithTtl uses 'with' expression which creates a new record instance (line 42)");
        }

        #endregion
    }
}
