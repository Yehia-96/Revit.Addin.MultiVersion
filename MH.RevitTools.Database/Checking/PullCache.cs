using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;
using Newtonsoft.Json;

namespace MH.RevitTools.Database.Checking
{
    /// <summary>
    /// The last pull of each project, kept per person on their PC in <c>%LOCALAPPDATA%\MH\RevitTools\Pulls</c>,
    /// so the pane can still show the database's values, and when they were read, while the database
    /// can't be reached. One file per database and project: a dev model never sees the office's values.
    /// Each file also names the project's number and sub-number it was checked against, and is only used
    /// for a link to that same project. The files hold project values only, never the connection file's
    /// login or password.
    /// </summary>
    public sealed class PullCache
    {
        // Bumped when the file's layout changes; older files are then ignored and written anew.
        private const int Version = 3;

        // A project is a few thousand values; anything far bigger isn't ours.
        private const long MaxFileBytes = 64L * 1024 * 1024;

        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
        };

        private readonly string _folder;
        private readonly string _databaseKey;

        public PullCache(string folder, ConnectionSettings settings)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("A folder is required.", nameof(folder));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            _folder = folder;
            _databaseKey = KeyFor(settings);
        }

        /// <summary><c>%LOCALAPPDATA%\MH\RevitTools\Pulls</c>.</summary>
        public static string DefaultFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MH", "RevitTools", "Pulls");

        /// <summary>
        /// Names the database: the server and database, nothing about the sign-in. The same for every PC
        /// that uses the same connection, and never the same for two databases.
        /// </summary>
        public static string KeyFor(ConnectionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            string name = settings.Server.Trim().ToUpperInvariant() + "|" + settings.Database.Trim().ToUpperInvariant();
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(name));
                var key = new StringBuilder();
                for (int i = 0; i < 8; i++)
                    key.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return key.ToString();
            }
        }

        /// <summary>The file for a project of this database.</summary>
        public string PathFor(int projectId) =>
            Path.Combine(_folder, _databaseKey + "-" + projectId.ToString(CultureInfo.InvariantCulture) + ".json");

        /// <summary>
        /// Keeps the pull, read for the project the link names (its number and sub-number checked against the
        /// database). False, with the reason, when it can't be written; the pane still has it in memory.
        /// </summary>
        public bool TrySave(LinkProjectData data, ModelLink link, DateTime readAtUtc, out string problem)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (link == null)
                throw new ArgumentNullException(nameof(link));
            if (link.ProjectId != data.ProjectId)
                throw new ArgumentException("The link is for another project than the data.", nameof(link));

            string path = PathFor(data.ProjectId);
            try
            {
                Directory.CreateDirectory(_folder);
                SafeFile.WriteAllText(path, JsonConvert.SerializeObject(PullFile.From(data, link, readAtUtc), Formatting.None, Json));
                problem = null;
                return true;
            }
            catch (IOException ex)
            {
                problem = "Couldn't keep the last pull in " + path + ": " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                problem = "You don't have permission to keep the last pull in " + _folder + ".";
                return false;
            }
        }

        /// <summary>
        /// The linked project's last pull; null when there's none, it can't be read, or it was kept for
        /// another project number under the same id.
        /// </summary>
        public CachedPull Load(ModelLink link)
        {
            if (link == null)
                throw new ArgumentNullException(nameof(link));

            string path = PathFor(link.ProjectId);
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaxFileBytes)
                    return null;
                PullFile file = JsonConvert.DeserializeObject<PullFile>(File.ReadAllText(path, Encoding.UTF8), Json);
                bool forThisProject = file != null && file.Version == Version && file.ProjectId == link.ProjectId
                    && file.Number == link.Number && file.SubNumber == link.SubNumber;
                return forThisProject ? file.ToPull() : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                // A value the data refuses, e.g. a target that isn't Object or Material.
                return null;
            }
        }

        /// <summary>
        /// Forgets the project's last pull, as when the database no longer has the project, or its id means
        /// another one now. False, with the reason, when the file stays.
        /// </summary>
        public bool TryRemove(int projectId, out string problem)
        {
            string path = PathFor(projectId);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                problem = null;
                return true;
            }
            catch (IOException ex)
            {
                problem = "Couldn't remove the last pull " + path + ": " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                problem = "You don't have permission to remove the last pull " + path + ".";
                return false;
            }
        }

        // The file's layout. Plain fields, so a renamed property in the model classes can't break old files.
        private sealed class PullFile
        {
            public int Version;
            public int ProjectId;
            public int Number;
            public int SubNumber;
            public long Stamp;
            public DateTime ReadAtUtc;
            public List<ParameterLine> Parameters;
            public List<ValueLine> ObjectValues;
            public List<MaterialLine> Materials;
            public List<ValueLine> MaterialValues;
            public List<ObjectMaterialLine> ObjectMaterials;

            public static PullFile From(LinkProjectData data, ModelLink link, DateTime readAtUtc) => new PullFile
            {
                Version = PullCache.Version,
                ProjectId = data.ProjectId,
                Number = link.Number,
                SubNumber = link.SubNumber,
                Stamp = data.Stamp,
                ReadAtUtc = DateTime.SpecifyKind(readAtUtc, DateTimeKind.Utc),
                Parameters = data.Parameters.Select(ParameterLine.From).ToList(),
                ObjectValues = data.ObjectValues.Select(value => new ValueLine
                {
                    Key = value.BauteilId,
                    ParameterId = value.ParameterId,
                    Value = value.Value,
                    ChangedAtUtc = value.ChangedAtUtc,
                    ChangedBy = value.ChangedBy,
                }).ToList(),
                Materials = data.Materials.Select(MaterialLine.From).ToList(),
                MaterialValues = data.MaterialValues.Select(value => new ValueLine
                {
                    Key = value.MaterialId,
                    ParameterId = value.ParameterId,
                    Value = value.Value,
                    ChangedAtUtc = value.ChangedAtUtc,
                    ChangedBy = value.ChangedBy,
                }).ToList(),
                ObjectMaterials = data.ObjectMaterials.Select(material => new ObjectMaterialLine
                {
                    Key = material.BauteilId,
                    MaterialId = material.MaterialId,
                    Name = material.MaterialName,
                    ChangedAtUtc = material.ChangedAtUtc,
                    ChangedBy = material.ChangedBy,
                }).ToList(),
            };

            public CachedPull ToPull()
            {
                var data = new LinkProjectData(
                    ProjectId,
                    Stamp,
                    (Parameters ?? new List<ParameterLine>()).Select(line => line.ToParameter()),
                    (ObjectValues ?? new List<ValueLine>()).Select(line =>
                        new LinkObjectValue(line.Key, line.ParameterId, line.Value, AsUtc(line.ChangedAtUtc), line.ChangedBy)),
                    (Materials ?? new List<MaterialLine>()).Select(line => line.ToMaterial()),
                    (MaterialValues ?? new List<ValueLine>()).Select(line =>
                        new LinkMaterialValue(line.Key, line.ParameterId, line.Value, AsUtc(line.ChangedAtUtc), line.ChangedBy)),
                    (ObjectMaterials ?? new List<ObjectMaterialLine>()).Select(line =>
                        new LinkObjectMaterial(line.Key, line.MaterialId, line.Name, AsUtc(line.ChangedAtUtc), line.ChangedBy)));
                return new CachedPull(data, DateTime.SpecifyKind(ReadAtUtc, DateTimeKind.Utc));
            }

            private static DateTime? AsUtc(DateTime? time) =>
                time.HasValue ? DateTime.SpecifyKind(time.Value, DateTimeKind.Utc) : (DateTime?)null;
        }

        private sealed class ParameterLine
        {
            public int Id;
            public Guid Guid;
            public string RevitName;
            public string SetCode;
            public string GroupCode;
            public string Name;
            public string DataType;
            public string Unit;
            public string Target;
            public List<string> Categories;

            public static ParameterLine From(LinkParameter parameter) => new ParameterLine
            {
                Id = parameter.ParameterId,
                Guid = parameter.Guid,
                RevitName = parameter.RevitName,
                SetCode = parameter.SetCode,
                GroupCode = parameter.GroupCode,
                Name = parameter.Name,
                DataType = parameter.DataType,
                Unit = parameter.Unit,
                Target = parameter.Target.ToString(),
                Categories = parameter.Categories.ToList(),
            };

            public LinkParameter ToParameter()
            {
                LinkTarget target;
                if (!Enum.TryParse(Target, false, out target) || !Enum.IsDefined(typeof(LinkTarget), target))
                    throw new ArgumentException("Parameter " + Id + " has an unknown target in the last pull.");
                return new LinkParameter(Id, Guid, RevitName, SetCode, GroupCode, Name, DataType, Unit, target, Categories);
            }
        }

        private sealed class MaterialLine
        {
            public string MaterialId;
            public string Name;
            public string GroupCode;
            public string Group;
            public string SurfacePattern;
            public string CutPattern;

            public static MaterialLine From(LinkMaterial material) => new MaterialLine
            {
                MaterialId = material.MaterialId,
                Name = material.Name,
                GroupCode = material.GroupCode,
                Group = material.Group,
                SurfacePattern = material.SurfacePattern,
                CutPattern = material.CutPattern,
            };

            public LinkMaterial ToMaterial() => new LinkMaterial(MaterialId, Name, GroupCode, Group, SurfacePattern, CutPattern);
        }

        private sealed class ValueLine
        {
            public string Key;
            public int ParameterId;
            public string Value;
            public DateTime? ChangedAtUtc;
            public string ChangedBy;
        }

        private sealed class ObjectMaterialLine
        {
            public string Key;
            public string MaterialId;
            public string Name;
            public DateTime? ChangedAtUtc;
            public string ChangedBy;
        }
    }

    /// <summary>A project's last pull, as <see cref="PullCache"/> kept it.</summary>
    public sealed class CachedPull
    {
        public CachedPull(LinkProjectData data, DateTime readAtUtc)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            ReadAtUtc = readAtUtc;
        }

        public LinkProjectData Data { get; }

        /// <summary>When the database answered with it, in UTC.</summary>
        public DateTime ReadAtUtc { get; }
    }
}
