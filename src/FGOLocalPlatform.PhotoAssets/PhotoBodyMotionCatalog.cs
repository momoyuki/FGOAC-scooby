using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace FGOLocalPlatform.PhotoAssets;

public static class PhotoBodyMotionCatalog
{
	private static readonly Regex Variant = new Regex("_S\\d{2}(?=_|\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static bool MatchesModel(string entry, string model, IEnumerable<string> names)
	{
		Match match = Variant.Match(model);
		Match match2 = Variant.Match(entry);
		if (!match.Success || !match2.Success)
		{
			return true;
		}
		if (match2.Value.Equals(match.Value, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (!match2.Value.Equals("_S01", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		string value = entry.Substring(0, match2.Index) + match.Value + entry.Substring(match2.Index + match2.Length);
		return !names.Contains<string>(value, StringComparer.OrdinalIgnoreCase);
	}

	public static bool IsCompatible(FgoMotion motion, int jointType, IReadOnlyList<PhotoRigBone> bones)
	{
		bool flag = ((jointType < 0 || jointType > 11) ? true : false);
		if (flag || !motion.IsPlayable || Normalize(jointType) != Normalize(motion.JointType) || bones.Count != motion.Tracks.Count)
		{
			return false;
		}
		PhotoRigBone[] array = new PhotoRigBone[bones.Count];
		foreach (PhotoRigBone bone in bones)
		{
			if ((uint)bone.Index >= (uint)array.Length || array[bone.Index] != null || string.IsNullOrEmpty(bone.Name))
			{
				flag = false;
				goto IL_012a;
			}
			array[bone.Index] = bone;
		}
		foreach (FgoMotionBoneTrack track in motion.Tracks)
		{
			if ((uint)track.BoneIndex >= (uint)array.Length)
			{
				flag = false;
			}
			else
			{
				PhotoRigBone photoRigBone = array[track.BoneIndex];
				if (!(photoRigBone == null) && photoRigBone.Parent == track.ParentIndex && string.Equals(photoRigBone.Name, track.Name, StringComparison.Ordinal))
				{
					continue;
				}
				flag = false;
			}
			goto IL_012a;
		}
		return true;
		IL_012a:
		return flag;
	}

	private static int Normalize(int value)
	{
		if (value <= 5)
		{
			return value;
		}
		return value - 6;
	}

	public static IReadOnlyList<PhotoBodyMotionEntry> ListActor(string archivePath, int jointType, IReadOnlyList<PhotoRigBone> bones, string modelResource = "")
	{
		string directoryName = Path.GetDirectoryName(archivePath);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(archivePath);
		Match actor = Regex.Match(fileNameWithoutExtension, "^mot_svt_\\d{4}(?=_|$)", RegexOptions.IgnoreCase);
		if (!actor.Success)
		{
			return List(archivePath, jointType, bones, modelResource);
		}
		return (from p in Directory.EnumerateFiles(directoryName, actor.Value + "*.farc")
			where Path.GetFileNameWithoutExtension(p).Equals(actor.Value, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(p).StartsWith(actor.Value + "_", StringComparison.OrdinalIgnoreCase)
			select p).OrderBy<string, string>((string p) => p, StringComparer.OrdinalIgnoreCase).SelectMany((string p) => List(p, jointType, bones, modelResource)).ToArray();
	}

	public static IReadOnlyList<PhotoBodyMotionEntry> List(string archivePath, int jointType, IReadOnlyList<PhotoRigBone> bones, string modelResource = "")
	{
		FarcArchive farcArchive = new FarcArchive(archivePath);
		List<PhotoBodyMotionEntry> list = new List<PhotoBodyMotionEntry>();
		foreach (FarcEntry item in farcArchive.Matching(".mot"))
		{
			try
			{
				FgoMotion fgoMotion = new FgoMotionParser().Parse(farcArchive.Read(item), item.Name);
				bool flag = IsCompatible(fgoMotion, jointType, bones) && MatchesModel(item.Name, modelResource, farcArchive.Entries.Select((FarcEntry e) => e.Name));
				list.Add(new PhotoBodyMotionEntry(farcArchive.Path, item.Name, fgoMotion.SkeletonName, fgoMotion.FrameCount, flag, flag ? "Skeleton matches" : "Skeleton does not match"));
			}
			catch (FgoFormatException ex)
			{
				list.Add(new PhotoBodyMotionEntry(farcArchive.Path, item.Name, "", 0, Compatible: false, ex.Message));
			}
		}
		return list;
	}

	public static FgoMotion Load(PhotoBodyMotionEntry entry, int jointType, IReadOnlyList<PhotoRigBone> bones, string modelResource = "")
	{
		FarcArchive farcArchive = new FarcArchive(entry.ArchivePath);
		FarcEntry farcEntry = farcArchive.Entries.SingleOrDefault((FarcEntry e) => e.Name == entry.EntryName) ?? throw new FgoFormatException("That motion is no longer in the archive.");
		FgoMotion fgoMotion = new FgoMotionParser().Parse(farcArchive.Read(farcEntry), farcEntry.Name);
		if (!IsCompatible(fgoMotion, jointType, bones) || !MatchesModel(farcEntry.Name, modelResource, farcArchive.Entries.Select((FarcEntry e) => e.Name)))
		{
			throw new FgoFormatException("This motion does not fit the character's skeleton.");
		}
		return fgoMotion;
	}

	public static float[] Sample(FgoMotion motion, float frame, ReadOnlySpan<float> original)
	{
		if (!float.IsFinite(frame) || original.Length != motion.Tracks.Count * 10)
		{
			throw new ArgumentException("The animation time or the original pose is not valid.");
		}
		float[] array = original.ToArray();
		frame = Math.Clamp(frame, 0f, Math.Max(0, motion.FrameCount - 1));
		foreach (FgoMotionBoneTrack track in motion.Tracks)
		{
			int num = checked(track.BoneIndex * 10);
			for (ushort num2 = 0; num2 < 3; num2++)
			{
				FgoMotionChannel fgoMotionChannel = track.Find(num2);
				if (fgoMotionChannel != null && fgoMotionChannel.HasScalarPayload)
				{
					array[num + 4 + num2] = fgoMotionChannel.EvaluateScalar(frame);
				}
				FgoMotionChannel fgoMotionChannel2 = track.Find((ushort)(num2 + 6));
				if (fgoMotionChannel2 != null && fgoMotionChannel2.HasScalarPayload)
				{
					array[num + 7 + num2] = fgoMotionChannel2.EvaluateScalar(frame);
				}
			}
			FgoMotionChannel fgoMotionChannel3 = track.FindRotation();
			if (fgoMotionChannel3 != null && fgoMotionChannel3.Frames.Length != 0 && fgoMotionChannel3.PackedValues.Length != 0)
			{
				Quaternion quaternion = fgoMotionChannel3.EvaluateRotation(frame);
				array[num] = quaternion.X;
				array[num + 1] = quaternion.Y;
				array[num + 2] = quaternion.Z;
				array[num + 3] = quaternion.W;
			}
		}
		foreach (float value in array)
		{
			if (!float.IsFinite(value))
			{
				throw new FgoFormatException("The motion pose holds values that are not finite numbers.");
			}
		}
		return array;
	}
}
