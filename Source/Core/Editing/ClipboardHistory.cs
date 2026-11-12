using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace CodeImp.DoomBuilder.Editing
{
	public sealed class ClipboardHistoryEntry
	{
		public DateTime CopiedUtc { get; internal set; }
		public string SourceMap { get; internal set; }
		public int Vertices { get; private set; }
		public int Sectors { get; private set; }
		public int Linedefs { get; private set; }
		public int Things { get; private set; }
		public double Width { get; internal set; }
		public double Height { get; internal set; }
		internal byte[] Payload { get; private set; }
		internal byte[] Hash { get; private set; }

		internal ClipboardHistoryEntry(byte[] payload, byte[] hash, int[] counts)
		{
			Payload = payload;
			Hash = hash;
			Vertices = counts[0];
			Sectors = counts[1];
			Linedefs = counts[2];
			Things = counts[3];
		}
	}

	public enum ClipboardHistoryAddResult { Added, MovedToTop, Disabled, TooLarge, Invalid }

	public sealed class ClipboardHistory
	{
		private readonly List<ClipboardHistoryEntry> entries = new List<ClipboardHistoryEntry>();
		private readonly ReadOnlyCollection<ClipboardHistoryEntry> view;
		private readonly long maxentrybytes;
		private readonly long maxtotalbytes;
		private long totalbytes;
		private int capacity;

		public event EventHandler Changed;
		public IReadOnlyList<ClipboardHistoryEntry> Entries { get { return view; } }
		public int Capacity
		{
			get { return capacity; }
			set
			{
				capacity = Math.Max(0, Math.Min(100, value));
				if(Prune()) OnChanged();
			}
		}

		public ClipboardHistory(int capacity, long maxentrybytes = 16L << 20, long maxtotalbytes = 64L << 20)
		{
			if(maxentrybytes < 0) throw new ArgumentOutOfRangeException("maxentrybytes");
			if(maxtotalbytes < 0) throw new ArgumentOutOfRangeException("maxtotalbytes");
			this.maxentrybytes = maxentrybytes;
			this.maxtotalbytes = maxtotalbytes;
			view = entries.AsReadOnly();
			Capacity = capacity;
		}

		public ClipboardHistoryAddResult Add(byte[] payload, string sourcemap, double width, double height, DateTime utcnow)
		{
			if(capacity == 0) return ClipboardHistoryAddResult.Disabled;
			if(payload == null || payload.Length < 16) return ClipboardHistoryAddResult.Invalid;
			if(payload.LongLength > maxentrybytes) return ClipboardHistoryAddResult.TooLarge;
			int[] counts = new int[4];
			using(BinaryReader reader = new BinaryReader(new MemoryStream(payload, false)))
			{
				for(int i = 0; i < counts.Length; i++)
				{
					counts[i] = reader.ReadInt32();
					if(counts[i] < 0) return ClipboardHistoryAddResult.Invalid;
				}
			}

			byte[] hash;
			using(SHA1 sha = SHA1.Create()) hash = sha.ComputeHash(payload);
			ClipboardHistoryEntry entry = entries.Find(e => e.Hash.SequenceEqual(hash) && e.Payload.SequenceEqual(payload));
			bool duplicate = entry != null;
			if(duplicate) entries.Remove(entry);
			else
			{
				entry = new ClipboardHistoryEntry(payload, hash, counts);
				totalbytes += payload.LongLength;
			}
			entry.CopiedUtc = utcnow;
			entry.SourceMap = sourcemap ?? string.Empty;
			entry.Width = width;
			entry.Height = height;
			entries.Insert(0, entry);
			Prune();
			OnChanged();
			return duplicate ? ClipboardHistoryAddResult.MovedToTop : ClipboardHistoryAddResult.Added;
		}

		public void MoveToTop(ClipboardHistoryEntry entry)
		{
			int index = entries.IndexOf(entry);
			if(index <= 0) return;
			entries.RemoveAt(index);
			entries.Insert(0, entry);
			OnChanged();
		}

		public void Remove(ClipboardHistoryEntry entry)
		{
			if(!entries.Remove(entry)) return;
			totalbytes -= entry.Payload.LongLength;
			OnChanged();
		}

		public void Clear()
		{
			if(entries.Count == 0) return;
			entries.Clear();
			totalbytes = 0;
			OnChanged();
		}

		private bool Prune()
		{
			bool changed = false;
			while(entries.Count > capacity || totalbytes > maxtotalbytes)
			{
				int last = entries.Count - 1;
				totalbytes -= entries[last].Payload.LongLength;
				entries.RemoveAt(last);
				changed = true;
			}
			return changed;
		}

		private void OnChanged()
		{
			if(Changed != null) Changed(this, EventArgs.Empty);
		}
	}
}
