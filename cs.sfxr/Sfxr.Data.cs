using System.Windows.Forms;

namespace cs.sfxr
{
    public partial class Sfxr : Form
    {
        public class F
        {
            private readonly float v;
            public F(float v) { this.v = v; }
            public static implicit operator float(F f) => f.v;
            public static implicit operator F(float v) => new F(v);
        }

        public struct Spriteset
        {
            public int[] Data;
            public int Width;
            public int Height;
            public int Pitch;
        };

        public struct Category
        {
            public string Name;
        };
    }
}
