using System.Runtime.InteropServices;
using Xunit;

namespace Svg.Skia.UnitTests;

public sealed class WindowsTheory : TheoryAttribute
{
    public WindowsTheory(string message = "Windows only theory")
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Skip = message;
        }
    }
}
