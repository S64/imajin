using System.Collections.Generic;

namespace imajin {

    public class ChallengeResponse {

        public string challenge { get; set; }

    }

    public class ImagePost {
        
        public string token { get; set; }
        public string channel { get; set; }
        public string text { get; set; }

        public List<ImagePostAttachment> attachments { get; set; }

    }

    public class ImagePostAttachment {

        public string fallback { get; set; }
        public string image_url { get; set; }

    }

    public class TextPost {

        public string token { get; set; }
        public string channel { get; set; }
        public string text { get; set; }

    }

}