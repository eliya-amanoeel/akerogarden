using Microsoft.Playwright;

namespace AkeroGarden.E2E.Infrastructure;

/// <summary>
/// Shared checks for the code-managed page photos (design.md, Imagery): the photo really decoded,
/// and it is cropped to its frame rather than sizing it.
/// </summary>
public static class PagePhotoAssertions
{
    /// <summary>Scrolls to the photo (most are lazy) and asserts it decoded and exactly covers <paramref name="frame"/>.</summary>
    public static async Task LoadsAndFillsFrameAsync(ILocator img, ILocator frame, string what)
    {
        await img.ScrollIntoViewIfNeededAsync();

        // Decoded pixels, not just a present <img>: a 404, a wrong fingerprinted path or a CSP
        // block all leave the element in the DOM with a naturalWidth of 0.
        await Assertions.Expect(img).ToHaveJSPropertyAsync("complete", true);
        Assert.True(await img.EvaluateAsync<int>("i => i.naturalWidth") > 0, $"{what}: photo did not decode");

        // Against the frame's inside (clientWidth/Height), not its outer box: a bordered frame such as
        // .contact-photo is 2px larger outside than the area a photo can cover.
        var inner = await frame.EvaluateAsync<double[]>("f => [f.clientWidth, f.clientHeight]");
        var imgBox = await img.BoundingBoxAsync();
        Assert.NotNull(imgBox);
        Assert.True(Math.Abs(inner[0] - imgBox.Width) < 1 && Math.Abs(inner[1] - imgBox.Height) < 1,
            $"{what}: photo is {imgBox.Width}x{imgBox.Height} but its frame is {inner[0]}x{inner[1]} inside");
    }

    /// <summary>For frames shaped by an <c>aspect-ratio</c>: asserts the frame's outer box has that ratio (width ÷ height).</summary>
    public static async Task FrameAspectIsAsync(ILocator frame, double ratio, string what)
    {
        var box = await frame.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(Math.Abs(box.Width / box.Height - ratio) < 0.01,
            $"{what}: frame is {box.Width}x{box.Height}, not the {ratio:0.###} aspect it declares");
    }

    /// <summary>
    /// For frames shaped only by a <c>min-height</c>: asserts the frame is exactly that tall. Covering
    /// the frame is not enough on its own — an in-flow photo that stretches its frame covers it too,
    /// which is precisely the defect this guards against.
    /// </summary>
    public static async Task FrameHeightIsItsMinHeightAsync(ILocator frame, string what)
    {
        var heights = await frame.EvaluateAsync<double[]>(
            "f => [f.getBoundingClientRect().height, parseFloat(getComputedStyle(f).minHeight)]");
        Assert.True(Math.Abs(heights[0] - heights[1]) < 1,
            $"{what}: frame is {heights[0]}px tall but its min-height is {heights[1]}px — the photo is sizing the frame");
    }
}
