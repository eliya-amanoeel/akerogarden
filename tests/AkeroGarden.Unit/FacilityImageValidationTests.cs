using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure facility-image upload rule (NFR-26, FR-27):
/// JPEG/PNG/WebP only, max 5 MB, non-empty.
/// </summary>
public class FacilityImageValidationTests
{
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("IMAGE/JPEG")] // case-insensitive
    public void AcceptedFormats_WithinSize_AreValid(string contentType)
    {
        Assert.Null(FacilityService.ValidateImage(contentType, 1_000_000));
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectedFormats_AreInvalid(string? contentType)
    {
        Assert.NotNull(FacilityService.ValidateImage(contentType, 1_000_000));
    }

    [Fact]
    public void OverFiveMegabytes_IsInvalid()
    {
        Assert.NotNull(FacilityService.ValidateImage("image/png", FacilityService.MaxImageBytes + 1));
    }

    [Fact]
    public void ExactlyFiveMegabytes_IsValid()
    {
        Assert.Null(FacilityService.ValidateImage("image/png", FacilityService.MaxImageBytes));
    }

    [Fact]
    public void EmptyFile_IsInvalid()
    {
        Assert.NotNull(FacilityService.ValidateImage("image/png", 0));
    }

    [Fact]
    public void Facility_CanHardDelete_DecisionFollowsReferenceCount()
    {
        Assert.True(FacilityService.CanHardDelete(0));
        Assert.False(FacilityService.CanHardDelete(1));
    }

    [Fact]
    public void NewFacility_IsActiveByDefault()
    {
        Assert.True(new Facility().IsActive);
    }
}
