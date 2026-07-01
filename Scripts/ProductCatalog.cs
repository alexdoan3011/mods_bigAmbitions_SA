using System;
using System.Collections.Generic;
using System.Text;

namespace SearchAnything
{
    /// <summary>One product that is sold somewhere in the city.</summary>
    public struct ProductInfo
    {
        /// <summary>The raw item key, e.g. <c>ba:itemname_apple</c>.</summary>
        public string ItemName;

        /// <summary>The localized, human-readable product name.</summary>
        public string DisplayName;

        /// <summary>How many shops currently offer this product.</summary>
        public int SellerCount;

        /// <summary>The product's market price, or a negative value when unknown.</summary>
        public float Price;
    }

    /// <summary>One shop that sells a given product, with where it is and the price.</summary>
    public struct SellerInfo
    {
        public CityBuildingController Controller;

        /// <summary>Business name, or the street address when unnamed.</summary>
        public string DisplayName;

        /// <summary>The localized neighbourhood the shop sits in.</summary>
        public string Neighbourhood;

        /// <summary>The retail price the shop lists, or a negative value when unknown.</summary>
        public float Price;

        /// <summary>True when this is a wholesale / import-export seller (sells boxes).</summary>
        public bool IsWholesale;
    }

    /// <summary>One searchable place in the city (a building / business).</summary>
    public struct LocationEntry
    {
        public CityBuildingController Controller;

        /// <summary>Business name, or the street address when unnamed.</summary>
        public string DisplayName;

        /// <summary>The business type (if any) otherwise the building type, localized.</summary>
        public string TypeLabel;

        /// <summary>The localized neighbourhood the place sits in.</summary>
        public string Neighbourhood;

        /// <summary>The building size code shown in game, e.g. "C1" (size letter + version).</summary>
        public string SizeCode;

        /// <summary>Who owns / rents the building, e.g. "Owned by You", "Rented by Rival".</summary>
        public string Ownership;

        /// <summary>Lower-cased blob (name + type labels/keys + neighbourhood) used for matching.</summary>
        public string SearchText;
    }

    /// <summary>Whether a search result is a product or a place.</summary>
    public enum ResultKind { Product, Location }

    /// <summary>One row in the unified search results list (a product or a place).</summary>
    public struct SearchResult
    {
        public ResultKind Kind;

        /// <summary>Stable id used to track the selected row ("p:&lt;item&gt;" or "l:&lt;id&gt;").</summary>
        public string Id;

        /// <summary>The name shown on the row.</summary>
        public string DisplayName;

        // -- Product --
        public string ItemName;
        public float Price;
        public int SellerCount;

        /// <summary>Extra descriptive line shown under a product (e.g. a vehicle's category and features).</summary>
        public string Detail;

        // -- Location --
        public CityBuildingController Controller;
        public string TypeLabel;
        public string Neighbourhood;
        public string SizeCode;
        public string Ownership;
    }

    /// <summary>Splits search queries into tokens, honouring quoted phrases.</summary>
    public static class SearchQuery
    {
        /// <summary>
        /// Splits a query into lower-cased tokens. Whitespace separates tokens,
        /// but text wrapped in double quotes is kept together as a single token
        /// (a Google-style "exact phrase"), so the spaces inside the quotes are
        /// preserved instead of breaking the phrase apart.
        /// </summary>
        public static string[] Tokenize(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<string>();

            var tokens = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            foreach (char c in query)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ' ' && !inQuotes)
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString().ToLowerInvariant());
                        sb.Length = 0;
                    }
                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                tokens.Add(sb.ToString().ToLowerInvariant());

            return tokens.ToArray();
        }
    }

    /// <summary>Small text helpers for turning localization keys into readable labels.</summary>
    public static class LabelText
    {
        public static string Prettify(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "(none)";

            // Prefer the game's own localization so values read the same as the
            // rest of the UI (e.g. "Garment District", not "Garmentdistrict").
            // GetLocalization returns the (lowercased) key unchanged when no
            // translation exists, so fall back to manual prettifying then.
            try
            {
                string localized = Localizor.LocalizorManager.GetLocalization(raw);
                if (!string.IsNullOrEmpty(localized) &&
                    !string.Equals(localized, raw, StringComparison.OrdinalIgnoreCase))
                    return localized;
            }
            catch { /* fall back to manual prettify */ }

            return Fallback(raw);
        }

        private static string Fallback(string raw)
        {
            string s = raw;

            int colon = s.IndexOf(':');
            if (colon >= 0 && colon < s.Length - 1)
                s = s.Substring(colon + 1);

            // Drop a leading category prefix such as "itemname_" or "buildingtype_".
            int underscore = s.IndexOf('_');
            if (underscore >= 0 && underscore < s.Length - 1)
            {
                string prefix = s.Substring(0, underscore);
                if (prefix.EndsWith("type") || prefix.EndsWith("name") || prefix.EndsWith("category") ||
                    prefix == "neighbourhood" || prefix == "neighborhood")
                {
                    s = s.Substring(underscore + 1);
                }
            }

            s = s.Replace('_', ' ').Replace('-', ' ').Trim();
            if (s.Length == 0)
                return raw;

            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s);
        }
    }
}
