using System;
using System.Drawing;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Poe2ItemCompanion
{
    public class FormattedRun
    {
        public string Text { get; set; }
        public Color Color { get; set; }

        public FormattedRun(string text, Color color)
        {
            Text = text;
            Color = color;
        }
    }

    public static class PobStatParser
    {
        // Standard PoB color codes
        public static readonly Color PositiveColor = Color.FromArgb(0x33, 0xFF, 0x77); // ^x33FF77 / ^2
        public static readonly Color NegativeColor = Color.FromArgb(0xDD, 0x00, 0x22); // ^xDD0022 / ^1
        public static readonly Color NeutralColor = Color.FromArgb(0xC8, 0xC8, 0xC8);  // White / Default
        public static readonly Color SubduedColor = Color.FromArgb(0x88, 0x88, 0x88);  // Grey / ^8
        public static readonly Color HeaderColor = Color.FromArgb(0xEE, 0xEE, 0xEE);   // Bright White

        public static Color GetRarityColor(string rarity)
        {
            if (string.IsNullOrEmpty(rarity)) return NeutralColor;
            switch (rarity.ToUpperInvariant())
            {
                case "NORMAL":
                    return Color.FromArgb(0xC8, 0xC8, 0xC8);
                case "MAGIC":
                    return Color.FromArgb(0x88, 0x88, 0xFF);
                case "RARE":
                    return Color.FromArgb(0xFF, 0xFF, 0x77);
                case "UNIQUE":
                    return Color.FromArgb(0xAF, 0x60, 0x25);
                case "RELIC":
                    return Color.FromArgb(0x60, 0xC0, 0xB0);
                case "GEM":
                    return Color.FromArgb(0x1A, 0xA2, 0x9B);
                case "CURRENCY":
                    return Color.FromArgb(0xAA, 0x9E, 0x82);
                default:
                    return NeutralColor;
            }
        }

        public static List<FormattedRun> ParseFormattedLine(string rawLine, Color defaultColor)
        {
            var runs = new List<FormattedRun>();
            if (string.IsNullOrEmpty(rawLine))
            {
                return runs;
            }

            Color currentColor = defaultColor;
            int idx = 0;
            int len = rawLine.Length;

            while (idx < len)
            {
                int caret = rawLine.IndexOf('^', idx);
                if (caret == -1)
                {
                    // Rest of the string
                    string text = rawLine.Substring(idx);
                    if (!string.IsNullOrEmpty(text))
                    {
                        runs.Add(new FormattedRun(text, currentColor));
                    }
                    break;
                }

                if (caret > idx)
                {
                    string text = rawLine.Substring(idx, caret - idx);
                    if (!string.IsNullOrEmpty(text))
                    {
                        runs.Add(new FormattedRun(text, currentColor));
                    }
                }

                idx = caret + 1;
                if (idx >= len) break;

                char next = rawLine[idx];
                if (next == 'x' && idx + 6 < len)
                {
                    // ^xRRGGBB
                    string hex = rawLine.Substring(idx + 1, 6);
                    int rgb;
                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out rgb))
                    {
                        int r = (rgb >> 16) & 0xFF;
                        int g = (rgb >> 8) & 0xFF;
                        int b = rgb & 0xFF;
                        currentColor = Color.FromArgb(r, g, b);
                    }
                    idx += 7;
                }
                else if (char.IsDigit(next))
                {
                    switch (next)
                    {
                        case '0': currentColor = Color.Black; break;
                        case '1': currentColor = NegativeColor; break; // Red
                        case '2': currentColor = PositiveColor; break; // Green
                        case '3': currentColor = Color.FromArgb(0x44, 0x88, 0xFF); break; // Blue
                        case '4': currentColor = Color.FromArgb(0xFF, 0xE0, 0x40); break; // Yellow
                        case '5': currentColor = Color.FromArgb(0xFF, 0x99, 0x00); break; // Orange
                        case '6': currentColor = Color.FromArgb(0xBB, 0x77, 0xFF); break; // Purple
                        case '7': currentColor = Color.White; break;
                        case '8': currentColor = SubduedColor; break;
                        case '9': currentColor = Color.FromArgb(0x55, 0x55, 0x55); break;
                    }
                    idx += 1;
                }
                else
                {
                    // Literal caret
                    runs.Add(new FormattedRun("^", currentColor));
                }
            }

            return runs;
        }

        public static string StripEscapes(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Remove ^xRRGGBB and ^0-9
            s = Regex.Replace(s, @"\^x[0-9a-fA-F]{6}", "");
            s = Regex.Replace(s, @"\^[0-9]", "");
            return s;
        }
    }
}
