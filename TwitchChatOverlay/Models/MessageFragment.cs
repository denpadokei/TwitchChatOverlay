namespace TwitchChatOverlay.Models
{
    public class TextFragment
    {
        public string Text { get; set; }
    }

    public abstract class EmoteFragment
    {
        public string Text { get; set; }
        public string EmoteId { get; set; }
        public abstract string EmoteUrl { get; }
    }

    public class StaticEmoteFragment : EmoteFragment
    {
        public override string EmoteUrl =>
            $"https://static-cdn.jtvnw.net/emoticons/v2/{this.EmoteId}/default/dark/2.0";
    }

    public class AnimatedEmoteFragment : EmoteFragment
    {
        public override string EmoteUrl =>
            $"https://static-cdn.jtvnw.net/emoticons/v2/{this.EmoteId}/animated/dark/1.0";
    }
}
