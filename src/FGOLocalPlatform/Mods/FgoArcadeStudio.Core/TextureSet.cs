using System;
using System.Collections.Generic;

namespace FgoArcadeStudio.Core;

public sealed class TextureSet
{
	private readonly Dictionary<string, FgoTexture> _textures = new Dictionary<string, FgoTexture>(StringComparer.OrdinalIgnoreCase);

	public IReadOnlyDictionary<string, FgoTexture> Textures => _textures;

	public IReadOnlyList<FgoTexture> OrderedTextures { get; private set; } = Array.Empty<FgoTexture>();

	internal void SetOrderedTextures(IReadOnlyList<FgoTexture> textures)
	{
		OrderedTextures = textures;
		_textures.Clear();
		foreach (FgoTexture texture in textures)
		{
			_textures[texture.Name] = texture;
		}
	}

	public FgoTexture? Get(string? name)
	{
		if (name == null || !_textures.TryGetValue(name, out FgoTexture value))
		{
			return null;
		}
		return value;
	}
}
