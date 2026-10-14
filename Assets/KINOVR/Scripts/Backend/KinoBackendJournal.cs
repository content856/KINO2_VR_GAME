using System;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace KinoVR
{
    // One transaction per headset. A pending result prevents another session from replacing it.
    [Serializable]
    public sealed class KinoBackendTransaction
    {
        public string deviceId, campaignId, storeId, registrationId, nickname, gameSessionId, expiresAt;
        public string submissionId, rejectionCode;
        public int score;
        public bool starting, pendingScore, needsAbandon;
    }

    public sealed class KinoBackendJournal
    {
        readonly string directory, legacyPath;
        string latestPath;
        long sequence;
        bool latestIsReceipt, scanned;
        public KinoBackendJournal(string root, string apiBaseUrl)
        {
            using (var sha = SHA256.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(apiBaseUrl.TrimEnd('/')))).Replace("-", "").Substring(0, 16);
                directory = Path.Combine(root, hash);
                legacyPath = Path.Combine(directory, "active.json");
            }
        }
        void ScanOnce()
        {
            if (scanned) return;
            // Immutable entries avoid replacement races in Unity/Mono on Windows. A new
            // journal scans once; this instance advances its own committed sequence.
            if (!Directory.Exists(directory)) { scanned = true; return; }
            foreach (string file in Directory.GetFiles(directory, "*.json"))
            {
                string name = Path.GetFileName(file);
                bool receipt = name.StartsWith("receipt-", StringComparison.Ordinal);
                int prefix = receipt ? 8 : name.StartsWith("entry-", StringComparison.Ordinal) ? 6 : 0;
                if (prefix == 0 || name.Length < prefix + 22 || name[prefix + 20] != '-' ||
                    !long.TryParse(name.Substring(prefix, 20), NumberStyles.None, CultureInfo.InvariantCulture, out long value)) continue;
                if (latestPath != null && value == sequence) throw new IOException("Ambiguous backend transaction journal sequence.");
                if (latestPath == null || value > sequence)
                { sequence = value; latestPath = file; latestIsReceipt = receipt; }
            }
            scanned = true;
        }
        public KinoBackendTransaction Load()
        {
            ScanOnce();
            string path = latestPath ?? legacyPath;
            if (!File.Exists(path))
            {
                if (latestPath != null) throw new IOException("The latest backend transaction journal entry is missing.");
                return null;
            }
            // Validate receipts too: corruption must never silently release a pending score.
            var item = JsonUtility.FromJson<KinoBackendTransaction>(File.ReadAllText(path));
            if (item == null || string.IsNullOrEmpty(item.registrationId) || string.IsNullOrEmpty(item.campaignId) || string.IsNullOrEmpty(item.storeId))
                throw new IOException("Invalid backend transaction journal.");
            return latestIsReceipt ? null : item;
        }
        public void Save(KinoBackendTransaction item) => Commit(item, false);

        void Commit(KinoBackendTransaction item, bool receipt)
        {
            ScanOnce();
            Directory.CreateDirectory(directory);
            if (sequence == long.MaxValue) throw new IOException("The backend transaction journal sequence is exhausted.");
            long next = checked(sequence + 1);
            string path = Path.Combine(directory, (receipt ? "receipt-" : "entry-") + next.ToString("D20", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N") + ".json");
            string temp = path + ".tmp";
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(item, true));
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            File.Move(temp, path);
            sequence = next; latestPath = path; latestIsReceipt = receipt;
        }
        // Keep a receipt, including rejected scores; never silently discard an unsent result.
        public void Archive(KinoBackendTransaction item) => Commit(item, true);
    }
}
