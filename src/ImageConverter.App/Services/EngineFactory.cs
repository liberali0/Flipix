using ImageConverter.Core.Engine;
using ImageConverter.Engine;

namespace ImageConverter.App.Services;

/// <summary>변환 엔진 연결 지점.</summary>
public static class EngineFactory
{
    public static IConversionEngine Create() => new EngineAdapter(new ConversionEngine());
}
