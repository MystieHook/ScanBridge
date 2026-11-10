using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ScanBridge
{
    internal static class OutputNaming
    {
        internal static readonly string[] Styles = { "dateTime", "separated", "compact", "date", "custom", "template" };
        internal static readonly string[] Labels = { "Year-month-day-hour-minute-second", "Year-month-day_hour-minute-second", "Compact date and time", "Date only", "Custom name", "Custom template" };

        internal static string Stem(Profile profile, DateTime time)
        {
            string name = (profile.outputName ?? "").Trim();
            if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            string prefix = (profile.outputPrefix ?? "").Trim();
            ValidatePart(name, "Custom name"); ValidatePart(prefix, "Prefix");
            string value;
            switch (profile.outputNaming)
            {
                case "dateTime": value = time.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture); break;
                case "separated": value = time.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture); break;
                case "compact": value = time.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture); break;
                case "date": value = time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); break;
                case "custom": value = name; break;
                case "template":
                    value = Regex.Replace(profile.outputTemplate ?? "", @"\{([^{}]+)\}", match =>
                    {
                        string token = match.Groups[1].Value;
                        if (token == "name") return name;
                        if (token == "prefix") return prefix;
                        if (Array.IndexOf(new[] { "yyyy", "MM", "dd", "HH", "mm", "ss" }, token) >= 0)
                            return time.ToString(token, CultureInfo.InvariantCulture);
                        throw new InvalidDataException("Unknown filename token: {" + token + "}.");
                    });
                    if (value.Contains("{") || value.Contains("}")) throw new InvalidDataException("Check the filename template braces.");
                    break;
                default: throw new InvalidDataException("Choose a filename format.");
            }
            if (profile.outputNaming != "template" && prefix.Length > 0) value = prefix + "-" + value;
            ValidatePart(value, "Filename");
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Enter a filename or a nonempty template.");
            if (value.Length > 140) throw new InvalidDataException("The filename must be 140 characters or fewer before .pdf.");
            if (Regex.IsMatch(value, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("This filename is reserved by Windows. Choose another name.");
            return value;
        }

        private static void ValidatePart(string value, string label)
        {
            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.EndsWith(".") || value.EndsWith(" "))
                throw new InvalidDataException(label + " cannot contain / \\ : * ? \" < > |, control characters, or end with a dot or space.");
        }

        // File.Move is atomic and never replaces an existing document, including concurrent exports.
        internal static string Commit(string temporary, string directory, string stem)
        {
            for (int i = 0; i < 100000; i++)
            {
                string target = Path.Combine(directory, stem + (i == 0 ? "" : " (" + (i + 1).ToString(CultureInfo.InvariantCulture) + ")") + ".pdf");
                try { File.Move(temporary, target); return target; }
                catch (IOException) { if (!File.Exists(target)) throw; }
            }
            throw new IOException("Too many files with this name. Choose another name or prefix.");
        }
    }
}
