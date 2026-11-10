using System;
using System.IO;
using System.Web.Script.Serialization;

namespace ScanBridge
{
    public sealed class Profile
    {
        public int schemaVersion { get; set; } = 1;
        public string destination { get; set; } = "";
        public string format { get; set; } = "pdf";
        public int dpi { get; set; } = 300;
        public bool duplex { get; set; } = true;
        public string simplexSide { get; set; } = "front";
        [ScriptIgnore] public bool AcquireBothSides => duplex || simplexSide == "back";
        public bool removeBlankPages { get; set; } = true;
        public int rotation { get; set; }
        public bool reviewBeforeSaving { get; set; }
        public bool deskew { get; set; } = true;
        public string colorMode { get; set; } = "color";
        public string outputNaming { get; set; } = "dateTime";
        public string outputName { get; set; } = "Document";
        public string outputPrefix { get; set; } = "Scan";
        public string outputTemplate { get; set; } = "{prefix}-{name}-{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}";

        public void Validate()
        {
            if (schemaVersion != 1) throw new InvalidDataException("Unsupported profile version.");
            OutputNaming.Stem(this, DateTime.Now);
            if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathRooted(destination) ||
                !(destination.StartsWith(@"\\") || (destination.Length > 2 && destination[1] == ':' && (destination[2] == '\\' || destination[2] == '/'))))
                throw new InvalidDataException("Choose a full local folder path or network UNC path.");
            if (destination.Contains("%") || destination.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new InvalidDataException("Use a fixed folder path without environment variables or invalid characters.");
            if (dpi != 150 && dpi != 200 && dpi != 300 && dpi != 600) throw new InvalidDataException("Unsupported resolution.");
            if (rotation != 0 && rotation != 90 && rotation != 180 && rotation != 270) throw new InvalidDataException("Unsupported rotation.");
            if (format != "pdf") throw new InvalidDataException("Only PDF output is currently defined.");
            if (colorMode != "color" && colorMode != "gray") throw new InvalidDataException("Unsupported color mode.");
            if (simplexSide != "front" && simplexSide != "back") throw new InvalidDataException("Unsupported single-sided placement.");
            if (destination.IndexOf('\r') >= 0 || destination.IndexOf('\n') >= 0) throw new InvalidDataException("Invalid destination.");
        }
    }

    public sealed class ProfileStore
    {
        public static readonly string SharedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ScanBridge", "profiles.json");
        public string FilePath { get; }
        public ProfileStore(string path) { FilePath = path; }
        public Profile Load()
        {
            if (!File.Exists(FilePath)) return null;
            var profile = new JavaScriptSerializer().Deserialize<Profile>(File.ReadAllText(FilePath));
            if (profile == null) throw new InvalidDataException("Empty profile.");
            profile.Validate();
            return profile;
        }
        public void Save(Profile profile)
        {
            profile.Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string backup = temporary + ".bak";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(profile));
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, backup);
                else File.Move(temporary, FilePath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (File.Exists(backup)) File.Delete(backup);
            }
        }
    }
}
