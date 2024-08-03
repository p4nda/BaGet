using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core
{
    public class SymbolStorageService : ISymbolStorageService
    {
        private const string SymbolsPathPrefix = "symbols";
        private const string PdbContentType = "binary/octet-stream";

        private readonly ILogger<SymbolStorageService> _logger;
        private readonly IStorageService _storage;

        public SymbolStorageService(ILogger<SymbolStorageService> logger, IStorageService storage)
        {
            _logger = logger;
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public async Task SavePortablePdbContentAsync(
            string filename,
            string key,
            Stream pdbStream,
            CancellationToken cancellationToken)
        {
            var path = GetPathForKey(filename, key);
            var result = await _storage.PutAsync(path, pdbStream, PdbContentType, cancellationToken);

            if (result == StoragePutResult.Conflict)
            {
                throw new InvalidOperationException($"Could not save PDB {filename} {key} due to conflict");
            }
        }

        public async Task<Stream> GetPortablePdbContentStreamOrNullAsync(string filename, string key)
        {
            var path = GetPathForKey(filename, key);

            try
            {
                return await _storage.GetAsync(path);
            }
            catch
            {
                return null;
            }
        }

        private string GetPathForKey(string filename, string key)
        {
            // Ensure the filename doesn't try to escape out of the current directory.
            var tempPath = Path.GetDirectoryName(Path.GetTempPath()) ?? "/tmp";
            var expandedPath = Path.GetDirectoryName(Path.Combine(tempPath, filename));

            if (expandedPath != tempPath)
            {
                _logger.LogError("Invalid file name: '{Filename}' (can't escape the current directory)", filename);
                return null;
            }

            if (!key.All(char.IsLetterOrDigit))
            {
                _logger.LogError("Invalid key: '{key}' (must contain exclusively letters and digits)", key);
                return null;
            }

            // The key's first 32 characters are the GUID, the remaining characters are the age.
            // See: https://github.com/dotnet/symstore/blob/98717c63ec8342acf8a07aa5c909b88bd0c664cc/docs/specs/SSQP_Key_Conventions.md#portable-pdb-signature
            // Debuggers should always use the age "ffffffff", however Visual Studio 2019
            // users have reported other age values. We will ignore the age.
            key = string.Concat(key.AsSpan(0, 32), "ffffffff");

            return Path.Combine(
                SymbolsPathPrefix,
                filename.ToLowerInvariant(),
                key.ToLowerInvariant());
        }
    }
}
