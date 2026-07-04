using Blish_HUD.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using SpriteFontPlus;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;

namespace Blish_HUD {

    public class ContentService : GameService {

        private static readonly Logger Logger = Logger.GetLogger<ContentService>();

        private const string REF_FILE = "ref.dat";

        #region Load Static

        private static readonly ConcurrentDictionary<string, BitmapFont>  _loadedBitmapFonts  = new ConcurrentDictionary<string, BitmapFont>();
        private static readonly ConcurrentDictionary<string, Texture2D>   _loadedTextures     = new ConcurrentDictionary<string, Texture2D>();
        
        #endregion

        public static class Colors {
            public static readonly Color ColonialWhite = Color.FromNonPremultiplied(255, 238, 187, 255);
            public static readonly Color Chardonnay = Color.FromNonPremultiplied(255, 204, 119, 255);
            public static readonly Color OldLace = Color.FromNonPremultiplied(253, 246, 225, 255);

            public static readonly Color DullColor = Color.FromNonPremultiplied(150, 150, 150, 255);

            public static Color Darkened(float amt) {
                return Color.FromNonPremultiplied((int)(amt * 255), (int)(amt * 255), (int)(amt * 255), 255);
            }

        }

        public static class Textures {
            public static Texture2D Error { get; private set; }
            public static Texture2D Pixel { get; private set; }

            public static Texture2D TransparentPixel { get; private set; }

            public static void Load() {
                using (var ctx = Graphics.LendGraphicsDeviceContext(true)) {
                    Error = Content.GetTexture(@"common\error");

                    Pixel = new Texture2D(ctx.GraphicsDevice, 1, 1);
                    Pixel.SetData(new[] { Color.White });

                    TransparentPixel = new Texture2D(ctx.GraphicsDevice, 1, 1);
                    TransparentPixel.SetData(new[] { Color.Transparent });
                }
            }
        }

        private IDataReader _audioDataReader;

        private const int CJK_TEXTURE_SIZE = 2048;

        private static readonly Lazy<IReadOnlyList<CharacterRange>> _chineseUiFontRanges = new Lazy<IReadOnlyList<CharacterRange>>(GetChineseUiFontRanges);

        public BitmapFont DefaultFont12 => GetFont(FontFace.Menomonia, FontSize.Size12, FontStyle.Regular);

        public BitmapFont DefaultFont14 => GetFont(FontFace.Menomonia, FontSize.Size14, FontStyle.Regular);

        public BitmapFont DefaultFont16 => GetFont(FontFace.Menomonia, FontSize.Size16, FontStyle.Regular);

        public BitmapFont DefaultFont18 => GetFont(FontFace.Menomonia, FontSize.Size18, FontStyle.Regular);

        public BitmapFont DefaultFont32 => GetFont(FontFace.Menomonia, FontSize.Size32, FontStyle.Regular);

        public enum FontFace {
            Menomonia
        }

        public enum FontSize {
            Size8 = 8,
            Size11 = 11,
            Size12 = 12,
            Size14 = 14,
            Size16 = 16,
            Size18 = 18,
            Size20 = 20,
            Size22 = 22,
            Size24 = 24,
            Size32 = 32,
            Size34 = 34,
            Size36 = 36,
        }

        public enum FontStyle {
            Regular,
            Italic,
            Bold,
        }

        public ContentManager ContentManager => BlishHud.Instance.ActiveContentManager;

        public DatAssetCache DatAssetCache { get; private set; }

        internal ContentService() {
            SetServiceModules(this.DatAssetCache = new DatAssetCache(this));
        }

        protected override void Initialize() {
            // Typically occurs when Blish HUD is extracted without its dependencies.
            if (!File.Exists(ApplicationSettings.Instance.RefPath)) {
                Blish_HUD.Debug.Contingency.NotifyMissingRef();
            }
        }

        protected override void Load() {
            Textures.Load();
            var zipArchiveReader = new ZipArchiveReader(RefPath);
            _audioDataReader = zipArchiveReader.GetSubPath("audio");
        }

        // Temporary failsafe until all audio bugs are resolved
        private int _playRemainingAttempts = 3;

        public void PlaySoundEffectByName(string soundName) {
            if (_playRemainingAttempts <= 0) {
                // We keep failing to play sound effects - don't even bother
                return;
            }

            if (GameService.GameIntegration.Audio.AudioDevice == null) {
                // No device is set yet or there isn't one to use
                return;
            }

            try {
                const string SOUND_EFFECT_FILE_EXTENSION = ".wav";
                var          filePath                    = soundName + SOUND_EFFECT_FILE_EXTENSION;

                if (_audioDataReader.FileExists(filePath)) {
                    SoundEffect.FromStream(_audioDataReader.GetFileStream(filePath)).Play(GameService.GameIntegration.Audio.Volume, 0, 0);
                }

                _playRemainingAttempts = 3;
            } catch (Exception ex) {
                _playRemainingAttempts--;
                Logger.Warn(ex, "Failed to play sound effect.");
            }
        }

        private static string RefPath => ApplicationSettings.Instance.RefPath ?? REF_FILE;

        // Used while debugging since it's easier
        private static Texture2D TextureFromFile(string filepath) {
            if (File.Exists(filepath)) {
                using (var fileStream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    return TextureUtil.FromStreamPremultiplied(BlishHud.Instance.GraphicsDevice, fileStream);
                }
            } else return null;
        }

        private static Texture2D TextureFromFileSystem(string filepath) {
            var refPath = RefPath;
            if (!File.Exists(refPath)) {
                Logger.Warn("{refFileName} is missing!  Lots of assets will be missing!", refPath);
                return null;
            }

            using (var refFs = new FileStream(refPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                using (var refArchive = new ZipArchive(refFs, ZipArchiveMode.Read)) {
                    var refEntry = refArchive.GetEntry(filepath);

                    if (refEntry != null) {
                        using (var textureStream = refEntry.Open()) {
                            var textureCanSeek = new MemoryStream();
                            textureStream.CopyTo(textureCanSeek);

                            if (GameService.Graphics == null) {
                                return TextureUtil.FromStreamPremultiplied(BlishHud.Instance.GraphicsDevice, textureCanSeek);
                            } else {
                                return TextureUtil.FromStreamPremultiplied(textureCanSeek);
                            }
                        }
                    }

                    return null;
                }
            }
        }

        public MonoGame.Extended.TextureAtlases.TextureAtlas GetTextureAtlas(string textureAtlasName) {
            return GameService.Content.ContentManager.Load<MonoGame.Extended.TextureAtlases.TextureAtlas>(textureAtlasName);
        }

        public void PurgeTextureCache(string textureName) {
            _loadedTextures.TryRemove(textureName, out var _);
        }

        public Texture2D GetTexture(string textureName) {
            return GetTexture(textureName, Textures.Error);
        }

        public Texture2D GetTexture(string textureName, Texture2D defaultTexture) {
            if (textureName == null) return defaultTexture;

            if (_loadedTextures.TryGetValue(textureName, out var cachedTexture))
                return cachedTexture;

            if (File.Exists(textureName)) {
                return TextureFromFile(textureName);
            }

            cachedTexture = TextureFromFileSystem($"{textureName}.png");

            if (cachedTexture == null) {
                try {
                    cachedTexture = GameService.Content.ContentManager.Load<Texture2D>(textureName);
                } catch (ContentLoadException) {
                    Logger.Warn("Could not find {textureName} precompiled or in the ref archive.", textureName);
                }
            }

            cachedTexture = cachedTexture ?? defaultTexture;

            _loadedTextures.TryAdd(textureName, cachedTexture);

            return cachedTexture;
        }

        #region Render Service

        private const string RENDERSERVICE_REQUESTURL = "https://render.guildwars2.com/file/";

        private static readonly Regex _regexRenderServiceSignatureFileIdPair = new Regex(@"(.{40})\/(\d+)(?>\..*)?$", RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Retreives a texture from the Guild Wars 2 Render Service.
        /// </summary>
        /// <param name="signature">The SHA1 signature of the requested texture.</param>
        /// <param name="fileId">The file id of the requested texture.</param>
        /// <returns>A transparent texture that is later overwritten by the texture downloaded from the Render Service.</returns>
        /// <seealso cref="https://wiki.guildwars2.com/wiki/API:Render_service"/>
        public AsyncTexture2D GetRenderServiceTexture(string signature, string fileId) {
            return this.DatAssetCache.GetTextureFromAssetId(int.Parse(fileId));
        }

        /// <summary>
        /// Retreives a texture from the Guild Wars 2 Render Service.
        /// </summary>
        /// <param name="uriOrSignatureFileIdPair">Either the full Render Service URL or the signature and file id URI (e.g. "7554DCAF5A1EA1BDF5297352A203AF2357BE2B5B/498983").</param>
        /// <returns>A transparent texture that is later overwritten by the texture downloaded from the Render Service.</returns>
        /// <seealso cref="https://wiki.guildwars2.com/wiki/API:Render_service"/>
        public AsyncTexture2D GetRenderServiceTexture(string uriOrSignatureFileIdPair) {
            var splitUri = _regexRenderServiceSignatureFileIdPair.Match(uriOrSignatureFileIdPair);

            if (!splitUri.Success) {
                throw new ArgumentException($"Could not find signature / file id pair in provided '{uriOrSignatureFileIdPair}'.", nameof(uriOrSignatureFileIdPair));
            }

            string signature = splitUri.Groups[1].Value;
            string fileId    = splitUri.Groups[2].Value;

            return GetRenderServiceTexture(signature, fileId);
        }

        #endregion

        private static IReadOnlyList<CharacterRange> GetChineseUiFontRanges() {
            var ranges = new List<CharacterRange>(FontUtil.GetRanges(Gw2FontRanges.Default));

            foreach (var cjkRange in GetCharacterRanges(GetChineseUiCharacters())) {
                ranges.Add(cjkRange);
            }

            return ranges;
        }

        private static string GetChineseUiCharacters() {
            var characters = new HashSet<char>();
            var assembly   = typeof(ContentService).Assembly;
            var culture    = CultureInfo.GetCultureInfo("zh-CN");

            foreach (string resourceName in assembly.GetManifestResourceNames().Where(IsStringResourceName)) {
                string baseName = resourceName.Substring(0, resourceName.Length - ".resources".Length);

                try {
                    ResourceSet resourceSet = new ResourceManager(baseName, assembly).GetResourceSet(culture, true, true);

                    if (resourceSet == null) {
                        continue;
                    }

                    foreach (DictionaryEntry entry in resourceSet) {
                        if (entry.Value is string value) {
                            foreach (char character in value) {
                                if (character >= 0x2E80) {
                                    characters.Add(character);
                                }
                            }
                        }
                    }
                } catch (MissingManifestResourceException) {
                    Logger.Debug("Unable to inspect string resource {resourceName} for Chinese UI font characters.", resourceName);
                }
            }

            return new string(characters.OrderBy(c => c).ToArray());
        }

        private static bool IsStringResourceName(string resourceName) {
            return resourceName.StartsWith("Blish_HUD.Strings.", StringComparison.Ordinal)
                && resourceName.EndsWith(".resources", StringComparison.Ordinal);
        }

        private static IEnumerable<CharacterRange> GetCharacterRanges(string characters) {
            var sortedCharacters = characters.Distinct().OrderBy(c => c).ToList();

            if (sortedCharacters.Count == 0) {
                yield break;
            }

            char rangeStart = sortedCharacters[0];
            char previous   = rangeStart;

            for (int i = 1; i < sortedCharacters.Count; i++) {
                char current = sortedCharacters[i];

                if (current == previous + 1) {
                    previous = current;
                    continue;
                }

                yield return new CharacterRange(rangeStart, previous);

                rangeStart = current;
                previous   = current;
            }

            yield return new CharacterRange(rangeStart, previous);
        }

        private static bool IsChineseUiCulture() {
            return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetChineseFontFilePath(FontStyle style) {
            string fontFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string[] candidates = style == FontStyle.Bold
                                      ? new[] { "Dengb.ttf", "simhei.ttf", "NotoSansSC-VF.ttf", "Deng.ttf", "simsunb.ttf" }
                                      : new[] { "Deng.ttf", "simhei.ttf", "NotoSansSC-VF.ttf", "simsunb.ttf" };

            return candidates.Select(candidate => Path.Combine(fontFolder, candidate))
                             .FirstOrDefault(File.Exists);
        }

        private BitmapFont GetChineseFont(FontFace font, FontSize size, FontStyle style) {
            string fontPath = GetChineseFontFilePath(style);

            if (fontPath == null) {
                Logger.Warn("Unable to find a Windows CJK font. Falling back to {fontFace}.", font);
                return null;
            }

            string fullFontName = $"zh-cn-{Path.GetFileNameWithoutExtension(fontPath).ToLowerInvariant()}-{((int)size).ToString()}-{style.ToString().ToLowerInvariant()}";

            if (!_loadedBitmapFonts.ContainsKey(fullFontName)) {
                try {
                    using var ctx = GameService.Graphics.LendGraphicsDeviceContext();

                    var loadedFont = TtfFontBaker.Bake(File.ReadAllBytes(fontPath),
                                                       (int)size,
                                                       CJK_TEXTURE_SIZE,
                                                       CJK_TEXTURE_SIZE,
                                                       _chineseUiFontRanges.Value)
                                                .CreateSpriteFont(ctx.GraphicsDevice)
                                                .ToBitmapFont();

                    loadedFont.LetterSpacing = 0;
                    _loadedBitmapFonts.TryAdd(fullFontName, loadedFont);

                    return loadedFont;
                } catch (Exception ex) {
                    Logger.Warn(ex, "Unable to load Chinese UI font {fontPath}. Falling back to {fontFace}.", fontPath, font);
                    return null;
                }
            }

            return _loadedBitmapFonts[fullFontName];
        }

        public BitmapFont GetFont(FontFace font, FontSize size, FontStyle style) {
            if (font == FontFace.Menomonia && IsChineseUiCulture()) {
                var chineseFont = GetChineseFont(font, size, style);

                if (chineseFont != null) {
                    return chineseFont;
                }
            }

            string fullFontName = $"{font.ToString().ToLowerInvariant()}-{((int)size).ToString()}-{style.ToString().ToLowerInvariant()}";

            if (!_loadedBitmapFonts.ContainsKey(fullFontName)) {
                var loadedFont = this.ContentManager.Load<BitmapFont>($"fonts\\{font.ToString().ToLowerInvariant()}\\{fullFontName}");
                loadedFont.LetterSpacing = -1;
                _loadedBitmapFonts.TryAdd(fullFontName, loadedFont);

                return loadedFont;
            }

            return _loadedBitmapFonts[fullFontName];
        }

        protected override void Unload() {
            _loadedTextures.Clear();
            _loadedBitmapFonts.Clear();
        }

        protected override void Update(GameTime gameTime) { /* NOOP */ }

    }
}
