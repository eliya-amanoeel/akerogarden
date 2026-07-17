using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

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
    public void RejectedFormats_ReportTheFormatError(string? contentType)
    {
        // A valid size is used so ONLY the format rule can be the cause — assert the format message
        // specifically, not merely that some error came back (which the size/empty rules would also
        // satisfy if the checks were ever reordered or crossed).
        Assert.Equal(
            "Bilden måste vara i formatet JPEG, PNG eller WebP.",
            FacilityService.ValidateImage(contentType, 1_000_000));
    }

    [Fact]
    public void OverFiveMegabytes_ReportsTheSizeError()
    {
        // Valid format → the size rule is the only thing that can fire.
        Assert.Equal(
            "Bilden får vara högst 5 MB.",
            FacilityService.ValidateImage("image/png", FacilityService.MaxImageBytes + 1));
    }

    [Fact]
    public void ExactlyFiveMegabytes_IsValid()
    {
        Assert.Null(FacilityService.ValidateImage("image/png", FacilityService.MaxImageBytes));
    }

    [Fact]
    public void EmptyFile_ReportsTheEmptyError()
    {
        // Valid format, zero bytes → the empty rule, not the size or format one.
        Assert.Equal("Bilden är tom.", FacilityService.ValidateImage("image/png", 0));
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
