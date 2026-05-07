using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests
{
    public class BrainarrSettingsSamplingCovTests
    {
        #region SamplingShape Property Tests

        [Fact]
        public void SamplingShape_Getter_ReturnsDefaultValue_WhenNotSet()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingShape;

            // Assert
            result.Should().NotBeNull("because SamplingShape has a default value");
            result.MaxAlbumsPerGroupFloor.Should().Be(3, "because that is the default floor value");
            result.MaxRelaxedInflation.Should().Be(3.0, "because that is the default inflation value");
        }

        [Fact]
        public void SamplingShape_Setter_StoresValue_WhenSet()
        {
            // Arrange
            var settings = new BrainarrSettings();
            var newShape = new SamplingShape
            {
                MaxAlbumsPerGroupFloor = 5,
                MaxRelaxedInflation = 2.0
            };

            // Act
            settings.SamplingShape = newShape;

            // Assert
            settings.SamplingShape.MaxAlbumsPerGroupFloor.Should().Be(5, "because we set it to 5");
            settings.SamplingShape.MaxRelaxedInflation.Should().Be(2.0, "because we set it to 2.0");
        }

        [Fact]
        public void SamplingShape_Setter_UsesDefault_WhenSetToNull()
        {
            // Arrange
            var settings = new BrainarrSettings();
            settings.SamplingShape = new SamplingShape { MaxAlbumsPerGroupFloor = 7 };

            // Act
            settings.SamplingShape = null!;

            // Assert
            settings.SamplingShape.Should().NotBeNull("because null is replaced with default");
            settings.SamplingShape.MaxAlbumsPerGroupFloor.Should().Be(3, "because default is restored");
        }

        [Fact]
        public void EffectiveSamplingShape_ReturnsShape_WhenSet()
        {
            // Arrange
            var settings = new BrainarrSettings();
            var shape = new SamplingShape { MaxAlbumsPerGroupFloor = 8 };

            // Act
            settings.SamplingShape = shape;
            var result = settings.EffectiveSamplingShape;

            // Assert
            result.MaxAlbumsPerGroupFloor.Should().Be(8, "because EffectiveSamplingShape returns the set shape");
        }

        #endregion

        #region Artist Sampling Properties Tests

        [Fact]
        public void SamplingArtistSimilarTopPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingArtistSimilarTopPercent;

            // Assert - Line 32-33 in source: Similar = new ModeDistribution(TopPercent: 60, RecentPercent: 30)
            result.Should().Be(60, "because default Artist Similar TopPercent is 60");
        }

        [Fact]
        public void SamplingArtistSimilarTopPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingArtistSimilarTopPercent = 75;

            // Assert
            settings.SamplingArtistSimilarTopPercent.Should().Be(75, "because we set it to 75");
        }

        [Fact]
        public void SamplingArtistSimilarRecentPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingArtistSimilarRecentPercent;

            // Assert - Line 32-33 in source: Similar = new ModeDistribution(TopPercent: 60, RecentPercent: 30)
            result.Should().Be(30, "because default Artist Similar RecentPercent is 30");
        }

        [Fact]
        public void SamplingArtistSimilarRecentPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingArtistSimilarRecentPercent = 20;

            // Assert
            settings.SamplingArtistSimilarRecentPercent.Should().Be(20, "because we set it to 20");
        }

        [Fact]
        public void SamplingArtistAdjacentTopPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingArtistAdjacentTopPercent;

            // Assert - Line 33 in source: Adjacent = new ModeDistribution(TopPercent: 45, RecentPercent: 35)
            result.Should().Be(45, "because default Artist Adjacent TopPercent is 45");
        }

        [Fact]
        public void SamplingArtistAdjacentTopPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingArtistAdjacentTopPercent = 50;

            // Assert
            settings.SamplingArtistAdjacentTopPercent.Should().Be(50, "because we set it to 50");
        }

        [Fact]
        public void SamplingArtistAdjacentRecentPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingArtistAdjacentRecentPercent;

            // Assert - Line 33 in source: Adjacent = new ModeDistribution(TopPercent: 45, RecentPercent: 35)
            result.Should().Be(35, "because default Artist Adjacent RecentPercent is 35");
        }

        [Fact]
        public void SamplingArtistAdjacentRecentPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingArtistAdjacentRecentPercent = 30;

            // Assert
            settings.SamplingArtistAdjacentRecentPercent.Should().Be(30, "because we set it to 30");
        }

        [Fact]
        public void SamplingArtistExploratoryTopPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingArtistExploratoryTopPercent;

            // Assert - Line 34 in source: Exploratory = new ModeDistribution(TopPercent: 35, RecentPercent: 40)
            result.Should().Be(35, "because default Artist Exploratory TopPercent is 35");
        }

        [Fact]
        public void SamplingArtistExploratoryTopPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingArtistExploratoryTopPercent = 40;

            // Assert
            settings.SamplingArtistExploratoryTopPercent.Should().Be(40, "because we set it to 40");
        }

        [Fact]
        public void SamplingArtistExploratoryRecentPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingArtistExploratoryRecentPercent;

            // Assert - Line 34 in source: Exploratory = new ModeDistribution(TopPercent: 35, RecentPercent: 40)
            result.Should().Be(40, "because default Artist Exploratory RecentPercent is 40");
        }

        [Fact]
        public void SamplingArtistExploratoryRecentPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingArtistExploratoryRecentPercent = 35;

            // Assert
            settings.SamplingArtistExploratoryRecentPercent.Should().Be(35, "because we set it to 35");
        }

        #endregion

        #region Album Sampling Properties Tests

        [Fact]
        public void SamplingAlbumSimilarTopPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingAlbumSimilarTopPercent;

            // Assert - Line 49 in SamplingShape.cs: Similar = new ModeDistribution(TopPercent: 55, RecentPercent: 30)
            result.Should().Be(55, "because default Album Similar TopPercent is 55");
        }

        [Fact]
        public void SamplingAlbumSimilarTopPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingAlbumSimilarTopPercent = 65;

            // Assert
            settings.SamplingAlbumSimilarTopPercent.Should().Be(65, "because we set it to 65");
        }

        [Fact]
        public void SamplingAlbumSimilarRecentPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingAlbumSimilarRecentPercent;

            // Assert - Line 49 in SamplingShape.cs: Similar = new ModeDistribution(TopPercent: 55, RecentPercent: 30)
            result.Should().Be(30, "because default Album Similar RecentPercent is 30");
        }

        [Fact]
        public void SamplingAlbumSimilarRecentPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingAlbumSimilarRecentPercent = 25;

            // Assert
            settings.SamplingAlbumSimilarRecentPercent.Should().Be(25, "because we set it to 25");
        }

        [Fact]
        public void SamplingAlbumAdjacentTopPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingAlbumAdjacentTopPercent;

            // Assert - Line 50 in SamplingShape.cs: Adjacent = new ModeDistribution(TopPercent: 45, RecentPercent: 35)
            result.Should().Be(45, "because default Album Adjacent TopPercent is 45");
        }

        [Fact]
        public void SamplingAlbumAdjacentTopPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingAlbumAdjacentTopPercent = 55;

            // Assert
            settings.SamplingAlbumAdjacentTopPercent.Should().Be(55, "because we set it to 55");
        }

        [Fact]
        public void SamplingAlbumAdjacentRecentPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingAlbumAdjacentRecentPercent;

            // Assert - Line 50 in SamplingShape.cs: Adjacent = new ModeDistribution(TopPercent: 45, RecentPercent: 35)
            result.Should().Be(35, "because default Album Adjacent RecentPercent is 35");
        }

        [Fact]
        public void SamplingAlbumAdjacentRecentPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingAlbumAdjacentRecentPercent = 40;

            // Assert
            settings.SamplingAlbumAdjacentRecentPercent.Should().Be(40, "because we set it to 40");
        }

        [Fact]
        public void SamplingAlbumExploratoryTopPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingAlbumExploratoryTopPercent;

            // Assert - Line 51 in SamplingShape.cs: Exploratory = new ModeDistribution(TopPercent: 35, RecentPercent: 40)
            result.Should().Be(35, "because default Album Exploratory TopPercent is 35");
        }

        [Fact]
        public void SamplingAlbumExploratoryTopPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingAlbumExploratoryTopPercent = 30;

            // Assert
            settings.SamplingAlbumExploratoryTopPercent.Should().Be(30, "because we set it to 30");
        }

        [Fact]
        public void SamplingAlbumExploratoryRecentPercent_Getter_ReturnsDefaultValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingAlbumExploratoryRecentPercent;

            // Assert - Line 51 in SamplingShape.cs: Exploratory = new ModeDistribution(TopPercent: 35, RecentPercent: 40)
            result.Should().Be(40, "because default Album Exploratory RecentPercent is 40");
        }

        [Fact]
        public void SamplingAlbumExploratoryRecentPercent_Setter_UpdatesValue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingAlbumExploratoryRecentPercent = 45;

            // Assert
            settings.SamplingAlbumExploratoryRecentPercent.Should().Be(45, "because we set it to 45");
        }

        #endregion

        #region UpdateDistribution Clamping Tests

        [Fact]
        public void SamplingArtistSimilarTopPercent_ClampsToMax100_WhenSetAbove100()
        {
            // Arrange - Line 47 in source: var top = Math.Clamp(topPercent ?? distribution.TopPercent, 0, 100);
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingArtistSimilarTopPercent = 150;

            // Assert
            settings.SamplingArtistSimilarTopPercent.Should().Be(100, "because top is clamped to max 100");
        }

        [Fact]
        public void SamplingArtistSimilarTopPercent_ClampsToMin0_WhenSetBelow0()
        {
            // Arrange - Line 47 in source: var top = Math.Clamp(topPercent ?? distribution.TopPercent, 0, 100);
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingArtistSimilarTopPercent = -10;

            // Assert
            settings.SamplingArtistSimilarTopPercent.Should().Be(0, "because top is clamped to min 0");
        }

        [Fact]
        public void SamplingArtistSimilarRecentPercent_ClampsToRemaining_WhenExceeds100MinusTop()
        {
            // Arrange - Line 48 in source: var recent = Math.Clamp(recentPercent ?? distribution.RecentPercent, 0, 100 - top);
            var settings = new BrainarrSettings();
            settings.SamplingArtistSimilarTopPercent = 80;

            // Act
            settings.SamplingArtistSimilarRecentPercent = 50;

            // Assert - recent is clamped to 100 - top = 100 - 80 = 20
            settings.SamplingArtistSimilarRecentPercent.Should().Be(20, "because recent is clamped to 100 - top (100 - 80 = 20)");
        }

        [Fact]
        public void SamplingAlbumAdjacentTopPercent_ClampsTo100_WhenSetTo200()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingAlbumAdjacentTopPercent = 200;

            // Assert
            settings.SamplingAlbumAdjacentTopPercent.Should().Be(100, "because top is clamped to max 100");
        }

        [Fact]
        public void SamplingAlbumExploratoryRecentPercent_ClampsToZero_WhenTopIs100()
        {
            // Arrange
            var settings = new BrainarrSettings();
            settings.SamplingAlbumExploratoryTopPercent = 100;

            // Act
            settings.SamplingAlbumExploratoryRecentPercent = 50;

            // Assert - recent is clamped to 100 - top = 100 - 100 = 0
            settings.SamplingAlbumExploratoryRecentPercent.Should().Be(0, "because recent is clamped to 0 when top is 100");
        }

        #endregion

        #region SamplingMaxAlbumsPerGroupFloor Tests

        [Fact]
        public void SamplingMaxAlbumsPerGroupFloor_Getter_ReturnsDefaultValue()
        {
            // Arrange - Line 13 in SamplingShape.cs: MaxAlbumsPerGroupFloor { get; init; } = 3;
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingMaxAlbumsPerGroupFloor;

            // Assert
            result.Should().Be(3, "because default MaxAlbumsPerGroupFloor is 3");
        }

        [Fact]
        public void SamplingMaxAlbumsPerGroupFloor_Setter_UpdatesValue_WhenInValidRange()
        {
            // Arrange - Line 191 in source: Math.Clamp(value, 0, 10)
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingMaxAlbumsPerGroupFloor = 5;

            // Assert
            settings.SamplingMaxAlbumsPerGroupFloor.Should().Be(5, "because we set it to 5");
        }

        [Fact]
        public void SamplingMaxAlbumsPerGroupFloor_Setter_ClampsToMin0_WhenSetBelow0()
        {
            // Arrange - Line 191 in source: Math.Clamp(value, 0, 10)
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingMaxAlbumsPerGroupFloor = -5;

            // Assert
            settings.SamplingMaxAlbumsPerGroupFloor.Should().Be(0, "because value is clamped to min 0");
        }

        [Fact]
        public void SamplingMaxAlbumsPerGroupFloor_Setter_ClampsToMax10_WhenSetAbove10()
        {
            // Arrange - Line 191 in source: Math.Clamp(value, 0, 10)
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingMaxAlbumsPerGroupFloor = 15;

            // Assert
            settings.SamplingMaxAlbumsPerGroupFloor.Should().Be(10, "because value is clamped to max 10");
        }

        #endregion

        #region SamplingMaxRelaxedInflation Tests

        [Fact]
        public void SamplingMaxRelaxedInflation_Getter_ReturnsDefaultValue()
        {
            // Arrange - Line 15 in SamplingShape.cs: MaxRelaxedInflation { get; init; } = 3.0;
            var settings = new BrainarrSettings();

            // Act
            var result = settings.SamplingMaxRelaxedInflation;

            // Assert
            result.Should().Be(3.0, "because default MaxRelaxedInflation is 3.0");
        }

        [Fact]
        public void SamplingMaxRelaxedInflation_Setter_UpdatesValue_WhenInValidRange()
        {
            // Arrange - Line 199 in source: Math.Clamp(value, 1.0, 5.0)
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingMaxRelaxedInflation = 2.5;

            // Assert
            settings.SamplingMaxRelaxedInflation.Should().Be(2.5, "because we set it to 2.5");
        }

        [Fact]
        public void SamplingMaxRelaxedInflation_Setter_ClampsToMin1_WhenSetBelow1()
        {
            // Arrange - Line 199 in source: Math.Clamp(value, 1.0, 5.0)
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingMaxRelaxedInflation = 0.5;

            // Assert
            settings.SamplingMaxRelaxedInflation.Should().Be(1.0, "because value is clamped to min 1.0");
        }

        [Fact]
        public void SamplingMaxRelaxedInflation_Setter_ClampsToMax5_WhenSetAbove5()
        {
            // Arrange - Line 199 in source: Math.Clamp(value, 1.0, 5.0)
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingMaxRelaxedInflation = 7.5;

            // Assert
            settings.SamplingMaxRelaxedInflation.Should().Be(5.0, "because value is clamped to max 5.0");
        }

        #endregion

        #region PlanCacheCapacity Tests

        [Fact]
        public void PlanCacheCapacity_Getter_ReturnsDefaultValue()
        {
            // Arrange - Line 9 in CacheSettings.cs: DefaultCapacity = 256
            var settings = new BrainarrSettings();

            // Act
            var result = settings.PlanCacheCapacity;

            // Assert
            result.Should().Be(256, "because default PlanCacheCapacity is 256");
        }

        [Fact]
        public void PlanCacheCapacity_Setter_UpdatesValue_WhenInValidRange()
        {
            // Arrange - Lines 7-8 in CacheSettings.cs: MinCapacity = 16, MaxCapacity = 1024
            var settings = new BrainarrSettings();

            // Act
            settings.PlanCacheCapacity = 512;

            // Assert
            settings.PlanCacheCapacity.Should().Be(512, "because we set it to 512");
        }

        [Fact]
        public void PlanCacheCapacity_Setter_ClampsToMin16_WhenSetBelow16()
        {
            // Arrange - Line 36 in CacheSettings.cs: Math.Clamp(capacity, MinCapacity, MaxCapacity)
            var settings = new BrainarrSettings();

            // Act
            settings.PlanCacheCapacity = 5;

            // Assert
            settings.PlanCacheCapacity.Should().Be(16, "because value is clamped to min 16");
        }

        [Fact]
        public void PlanCacheCapacity_Setter_ClampsToMax1024_WhenSetAbove1024()
        {
            // Arrange - Line 36 in CacheSettings.cs: Math.Clamp(capacity, MinCapacity, MaxCapacity)
            var settings = new BrainarrSettings();

            // Act
            settings.PlanCacheCapacity = 2000;

            // Assert
            settings.PlanCacheCapacity.Should().Be(1024, "because value is clamped to max 1024");
        }

        #endregion

        #region PlanCacheTtlMinutes Tests

        [Fact]
        public void PlanCacheTtlMinutes_Getter_ReturnsDefaultValue()
        {
            // Arrange - Line 13 in CacheSettings.cs: DefaultTtlMinutes = 5
            var settings = new BrainarrSettings();

            // Act
            var result = settings.PlanCacheTtlMinutes;

            // Assert
            result.Should().Be(5, "because default PlanCacheTtlMinutes is 5");
        }

        [Fact]
        public void PlanCacheTtlMinutes_Setter_UpdatesValue_WhenInValidRange()
        {
            // Arrange - Lines 11-12 in CacheSettings.cs: MinTtlMinutes = 1, MaxTtlMinutes = 60
            var settings = new BrainarrSettings();

            // Act
            settings.PlanCacheTtlMinutes = 30;

            // Assert
            settings.PlanCacheTtlMinutes.Should().Be(30, "because we set it to 30");
        }

        [Fact]
        public void PlanCacheTtlMinutes_Setter_ClampsToMax60_WhenSetAbove60()
        {
            // Arrange - Lines 52-53 in CacheSettings.cs: max = TimeSpan.FromMinutes(MaxTtlMinutes)
            var settings = new BrainarrSettings();

            // Act
            settings.PlanCacheTtlMinutes = 120;

            // Assert
            settings.PlanCacheTtlMinutes.Should().Be(60, "because value is clamped to max 60 minutes");
        }

        [Fact]
        public void PlanCacheTtlMinutes_Setter_UsesDefault5_WhenSetToZeroOrNegative()
        {
            // Arrange - Lines 47-50 in CacheSettings.cs: if (value <= TimeSpan.Zero) return DefaultTtlMinutes
            var settings = new BrainarrSettings();

            // Act
            settings.PlanCacheTtlMinutes = 0;

            // Assert - TTL of 0 results in default 5 minutes
            settings.PlanCacheTtlMinutes.Should().Be(5, "because zero or negative TTL reverts to default 5 minutes");
        }

        #endregion

        #region PreferMinimalPromptFormatting Tests

        [Fact]
        public void PreferMinimalPromptFormatting_Getter_ReturnsDefaultFalse()
        {
            // Arrange - Line 220 in source: public bool PreferMinimalPromptFormatting { get; set; }
            var settings = new BrainarrSettings();

            // Act
            var result = settings.PreferMinimalPromptFormatting;

            // Assert
            result.Should().BeFalse("because default PreferMinimalPromptFormatting is false");
        }

        [Fact]
        public void PreferMinimalPromptFormatting_Setter_UpdatesValue_WhenSetToTrue()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.PreferMinimalPromptFormatting = true;

            // Assert
            settings.PreferMinimalPromptFormatting.Should().BeTrue("because we set it to true");
        }

        #endregion

        #region EffectiveCacheSettings Tests

        [Fact]
        public void EffectiveCacheSettings_ReturnsNormalizedSettings_WithDefaults()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = settings.EffectiveCacheSettings;

            // Assert
            result.Should().NotBeNull("because EffectiveCacheSettings always returns a valid object");
            result.PlanCacheCapacity.Should().Be(256, "because default capacity is 256");
            result.PlanCacheTtl.TotalMinutes.Should().Be(5, "because default TTL is 5 minutes");
        }

        [Fact]
        public void EffectiveCacheSettings_ReflectsUpdatedCapacity()
        {
            // Arrange
            var settings = new BrainarrSettings();
            settings.PlanCacheCapacity = 512;

            // Act
            var result = settings.EffectiveCacheSettings;

            // Assert
            result.PlanCacheCapacity.Should().Be(512, "because we updated capacity to 512");
        }

        #endregion

        #region Multiple Property Updates Independence Tests

        [Fact]
        public void SamplingProperties_AreIndependent_BetweenArtistAndAlbum()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingArtistSimilarTopPercent = 80;
            settings.SamplingAlbumSimilarTopPercent = 40;

            // Assert
            settings.SamplingArtistSimilarTopPercent.Should().Be(80, "because Artist settings are independent");
            settings.SamplingAlbumSimilarTopPercent.Should().Be(40, "because Album settings are independent");
        }

        [Fact]
        public void SamplingProperties_AreIndependent_BetweenModes()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingArtistSimilarTopPercent = 70;
            settings.SamplingArtistAdjacentTopPercent = 50;
            settings.SamplingArtistExploratoryTopPercent = 30;

            // Assert
            settings.SamplingArtistSimilarTopPercent.Should().Be(70, "because Similar mode is independent");
            settings.SamplingArtistAdjacentTopPercent.Should().Be(50, "because Adjacent mode is independent");
            settings.SamplingArtistExploratoryTopPercent.Should().Be(30, "because Exploratory mode is independent");
        }

        [Fact]
        public void SamplingProperties_TopAndRecentAreIndependent_WithinSameMode()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.SamplingArtistSimilarTopPercent = 50;
            settings.SamplingArtistSimilarRecentPercent = 25;

            // Assert
            settings.SamplingArtistSimilarTopPercent.Should().Be(50, "because Top is independent of Recent");
            settings.SamplingArtistSimilarRecentPercent.Should().Be(25, "because Recent is independent of Top");
        }

        #endregion
    }
}
