using System;
using Microsoft.Graphics.Canvas.Effects;

namespace Painting.Ink
{
    public enum BlendMode
    {
        // Default layer mode
        Normal,
        // Extra layer modes
        Dissolve,
        Multiply,
        Divide,
        Screen,
        Overlay,
        Dodge,
        Burn,
        HardLight,
        SoftLight,
        Difference,
        Addition,
        Substract,
        DarkenOnly,
        LightenOnly,
        Hue,
        Saturation,
        Color,
        Value
    }

    public static class BlendModeHelper
    {
        public static BlendEffectMode ToBlendEffectMode(this BlendMode mode)
        {
            switch (mode)
            {
                default:
                case BlendMode.Normal:
                case BlendMode.Addition:
                    throw new ArgumentException();
                case BlendMode.Dissolve:
                    return BlendEffectMode.Dissolve;
                case BlendMode.Multiply:
                    return BlendEffectMode.Multiply;
                case BlendMode.Divide:
                    return BlendEffectMode.Division;
                case BlendMode.Screen:
                    return BlendEffectMode.Screen;
                case BlendMode.Overlay:
                    return BlendEffectMode.Overlay;
                case BlendMode.Dodge:
                    return BlendEffectMode.ColorDodge;
                case BlendMode.Burn:
                    return BlendEffectMode.ColorBurn;
                case BlendMode.HardLight:
                    return BlendEffectMode.HardLight;
                case BlendMode.SoftLight:
                    return BlendEffectMode.SoftLight;
                case BlendMode.Difference:
                    return BlendEffectMode.Difference;
                case BlendMode.Substract:
                    return BlendEffectMode.Subtract;
                case BlendMode.DarkenOnly:
                    return BlendEffectMode.DarkerColor;
                case BlendMode.LightenOnly:
                    return BlendEffectMode.LighterColor;
                case BlendMode.Hue:
                    return BlendEffectMode.Hue;
                case BlendMode.Saturation:
                    return BlendEffectMode.Saturation;
                case BlendMode.Color:
                    return BlendEffectMode.Color;
                case BlendMode.Value:
                    return BlendEffectMode.Luminosity;
            }
        }
    }
}
