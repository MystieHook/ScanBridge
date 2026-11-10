using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ScanBridge
{
    public sealed class ScanPage
    {
        public string file { get; set; }
        public int rotation { get; set; }
        public bool excluded { get; set; }
        public bool blank { get; set; }
        public bool otherSide { get; set; }
    }
    public sealed class ScanJob
    {
        public string id { get; set; }
        public DateTime created { get; set; }
        public Profile profile { get; set; }
        public List<ScanPage> pages { get; set; } = new List<ScanPage>();
        public string state { get; set; } = "Pending";
        public string output { get; set; }
        public string error { get; set; }
        [ScriptIgnore] public string Folder { get; set; }
        internal string PagePath(ScanPage page)
        {
            if (Path.GetFileName(page.file) != page.file) throw new InvalidDataException("Invalid page path.");
            return Path.Combine(Folder, page.file);
        }
        internal void Save()
        {
            string path = Path.Combine(Folder, "job.json");
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        internal static ScanJob Load(string folder)
        {
            var job = new JavaScriptSerializer().Deserialize<ScanJob>(File.ReadAllText(Path.Combine(folder, "job.json")));
            if (job == null || job.profile == null || job.pages == null) throw new InvalidDataException("Invalid saved scan.");
            job.Folder = folder; job.profile.Validate(); return job;
        }
    }
    internal static class ScanJobs
    {
        internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScanBridge", "Jobs");
        internal static ScanJob Import(string resultFile, Profile profile, string root = null)
        {
            profile.Validate();
            var ini = ScanSnapHomeConnection.ReadIni(File.ReadAllLines(resultFile, Encoding.Default));
            string description;
            if(!ini.TryGetValue("INFO/FileDesc",out description)||description!="PFUFILELISTFORMAT")
                throw new InvalidDataException("The callback is not a ScanSnap result file.");
            int count;
            if (!ini.ContainsKey("FILES/FileCount") || !int.TryParse(ini["FILES/FileCount"], out count) || count < 1 || count > 2000)
                throw new InvalidDataException("Home returned no page files, or an invalid result file.");
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            var job = new ScanJob { id = id, created = DateTime.Now, profile = profile, Folder = Path.Combine(root ?? Root, id) };
            Directory.CreateDirectory(job.Folder);
            // Keep callback metadata for diagnosis, but do not log its contents.
            File.Copy(resultFile, Path.Combine(job.Folder, "vendor-result.ini"));
            try
            {
                bool keepBackOnly=!profile.duplex && profile.simplexSide=="back";
                string duplex;
                if(keepBackOnly && (count%2!=0 || !ini.TryGetValue("SCAN/Duplex",out duplex) || duplex!="1"))
                    throw new InvalidDataException("Facing-you scanning requires paired front/back pages. Home did not confirm a duplex batch; the source files have been kept.");
                for (int index = 1; index <= count; index++)
                {
                    string source;
                    if (!ini.TryGetValue("FILES/File" + index, out source)) throw new InvalidDataException("Home omitted page " + index + ".");
                    source = source.Trim('"');
                    if (!File.Exists(source)) throw new FileNotFoundException("A scanned page is unavailable.", source);
                    string extension = Path.GetExtension(source).ToLowerInvariant();
                    if (extension != ".jpg" && extension != ".jpeg")
                        throw new InvalidDataException("Home returned " + extension + " instead of JPEG pages. Restart Home after installing ScanBridge's integration.");
                    string file = "page-" + index.ToString("D4") + ".jpg";
                    File.Copy(source, Path.Combine(job.Folder, file));
                    using (var image = new System.Drawing.Bitmap(Path.Combine(job.Folder, file)))
                    {
                        bool blank = ImageProcessing.IsBlank(image);
                        bool otherSide=keepBackOnly && index%2==1;
                        job.pages.Add(new ScanPage { file = file, blank = blank, otherSide=otherSide, excluded = otherSide || (profile.removeBlankPages && blank), rotation = profile.rotation });
                    }
                }
                job.Save();
                return job;
            }
            catch (Exception ex)
            {
                job.state = "Import failed"; job.error = ex.Message; job.Save(); throw;
            }
            // Vendor source pages are deliberately retained during the integration pilot.
            // Never delete source documents supplied by an external callback.
        }
        internal static List<ScanJob> List()
        {
            var jobs = new List<ScanJob>();
            if (!Directory.Exists(Root)) return jobs;
            foreach (string folder in Directory.GetDirectories(Root))
                try { jobs.Add(ScanJob.Load(folder)); } catch (IOException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
            return jobs.OrderByDescending(j => j.created).ToList();
        }
        internal static string Export(ScanJob job)
        {
            if (!job.pages.Any(p => !p.excluded)) throw new InvalidOperationException("All pages are hidden. Include a page in Review before saving.");
            job.profile.Validate();
            string temporary = null;
            try
            {
                Directory.CreateDirectory(job.profile.destination);
                string stem = OutputNaming.Stem(job.profile, job.created);
                temporary = Path.Combine(job.profile.destination, ".ScanBridge-" + Guid.NewGuid().ToString("N") + ".partial");
                PdfWriter.Write(job, temporary);
                string target = OutputNaming.Commit(temporary, job.profile.destination, stem);
                job.state = "Saved"; job.output = target; job.error = null; job.Save(); return target;
            }
            catch (Exception ex)
            {
                job.state = "Needs attention"; job.error = ex.Message; job.Save(); throw;
            }
            finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
