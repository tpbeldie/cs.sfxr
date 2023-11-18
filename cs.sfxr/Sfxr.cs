using System;
using System.Media;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using System.ComponentModel;
using System.Drawing.Imaging;

namespace cs.sfxr
{
    [DesignerCategory("C#")]
    public partial class Sfxr : Form
    {
        /* ::::::::: Fields :::::::: */

        private SoundPlayer m_player = new SoundPlayer();
        private Random m_rand;
        private Spriteset m_font;
        private Spriteset m_ld48;
        private Category[] m_categories = new Category[10];
        public int[] m_ddkscreen32;
        private bool m_mute_stream;
        private int m_wave_type;
        private F p_base_freq;
        private F p_freq_limit;
        private F p_freq_ramp;
        private F p_freq_dramp;
        private F p_duty;
        private F p_duty_ramp;
        private F p_vib_strength;
        private F p_vib_speed;
        private F p_vib_delay;
        private F p_env_attack;
        private F p_env_sustain;
        private F p_env_decay;
        private F p_env_punch;
        private F p_lpf_resonance;
        private F p_lpf_freq;
        private F p_lpf_ramp;
        private F p_hpf_freq;
        private F p_hpf_ramp;
        private F p_pha_offset;
        private F p_pha_ramp;
        private F p_repeat_speed;
        private F p_arp_speed;
        private F p_arp_mod;
        private F m_master_vol = 0.05f;
        private F m_sound_vol = 0.5f;
        private bool m_filter_on;
        private bool m_playing_sample = false;
        private int m_phase;
        private double m_fperiod;
        private double m_fmaxperiod;
        private double m_fslide;
        private double m_fdslide;
        private int m_period;
        private float m_square_duty;
        private float m_square_slide;
        private int m_env_stage;
        private int m_env_time;
        private int[] m_env_length = new int[3];
        private float m_env_vol;
        private float m_fphase;
        private float m_fdphase;
        private int m_iphase;
        private float[] m_phaser_buffer = new float[1024];
        private int m_ipp;
        private float[] m_noise_buffer = new float[32];
        private float m_fltp;
        private float m_fltdp;
        private float m_fltw;
        private float m_fltw_d;
        private float m_fltdmp;
        private float m_fltphp;
        private float m_flthp;
        private float m_flthp_d;
        private float m_vib_phase;
        private float m_vib_speed;
        private float m_vib_amp;
        private int m_rep_time;
        private int m_rep_limit;
        private int m_arp_time;
        private int m_arp_limit;
        private double m_arp_mod;
        private float? m_vselected = null;
        private int m_vcurbutton = -1;
        private int m_wav_bits = 16;
        private int m_wav_freq = 44100;
        private int m_file_samples_written;
        private float m_file_sample = 0.0f;
        private int m_fileacc = 0;
        private int m_mouse_x;
        private int m_mouse_y;
        private int m_mouse_px;
        private int m_mouse_py;
        private bool m_mouse_left = false;
        private bool m_mouse_right = false;
        private bool m_mouse_middle = false;
        private bool m_mouse_left_click = false;
        private bool m_mouse_right_click = false;
        private bool m_mouse_middle_click = false;
        private Rectangle m_credits_area;
        private Point m_mousePosition;
        private Thread m_audio_thread;
        private SoundPlayer m_soundPlayer;
        private Bitmap m_bitmap;
        private int m_width;
        private int m_height;
        private int m_selected_slider_id = -1;

        /* ::::::::: Constructors :::::::: */

        public Sfxr() {
            m_width = 640;
            m_height = 480;
            m_ddkscreen32 = new int[m_width * m_height];
            m_view_port = new Rectangle(0, 0, m_width, m_height);
            m_bitmap = new Bitmap(m_width, m_height, PixelFormat.Format32bppArgb);
            m_credits_area = new Rectangle(m_width - 110, 10, 90, 10);
            InitializeAudio();
            DdkInit();
            Loop();
        }

        private void DdkInit() {
            FormBorderStyle = FormBorderStyle.Fixed3D;
            Size = new Size(m_width, m_height + 32 + 1);
            SetStyle(ControlStyles.DoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Text = "cs.sfxr";
            m_rand = new Random();
            if (LoadTGA(ref m_font, "font.tga")) {
                // Try again in the current working directory
                if (LoadTGA(ref m_font, "images/font.tga")) {
                    Console.Error.WriteLine("Error: Could not open /usr/local/share/sfxr/images/font.tga nor images/font.tga");
                    Environment.Exit(1);
                }
            }
            if (LoadTGA(ref m_ld48, "ld48.tga")) {
                // Try again in the current working directory
                if (LoadTGA(ref m_ld48, "images/ld48.tga")) {
                    Console.Error.WriteLine("Error: Could not open /usr/local/share/sfxr/images/ld48.tga nor images/ld48.tga");
                    Environment.Exit(1);
                }
            }
            m_ld48.Width = m_ld48.Pitch;
            m_categories[0].Name = "PICKUP/COIN";
            m_categories[1].Name = "LASER/SHOOT";
            m_categories[2].Name = "EXPLOSION";
            m_categories[3].Name = "POWERUP";
            m_categories[4].Name = "HIT/HURT";
            m_categories[5].Name = "JUMP";
            m_categories[6].Name = "BLIP/SELECT";
            m_player = new SoundPlayer();
            ResetParams();
            MouseDown += (s, e) => {
                if (m_credits_area.Contains(m_mousePosition)) {
                    System.Diagnostics.Process.Start(@"https://www.github.com/tpbeldie/");
                }
            };
        }

        /* ::::::::: Methods :::::::: */

        private void Loop() {
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer {
                Interval = 15
            };
            timer.Tick += (s, e) => {
                SdlUpdate();
                if (!DdkCalcFrame()) {
                    return;
                }
            };
            timer.Start();
        }

        private void SdlUpdate() {
            m_mouse_px = m_mouse_x;
            m_mouse_py = m_mouse_y;
            m_mousePosition = PointToClient(MousePosition);
            m_mouse_x = m_mousePosition.X;
            m_mouse_y = m_mousePosition.Y;
            m_mouse_left = (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left;
            m_mouse_right = (Control.MouseButtons & MouseButtons.Right) == MouseButtons.Right;
            m_mouse_middle = (Control.MouseButtons & MouseButtons.Middle) == MouseButtons.Middle;
            m_mouse_left_click = m_mouse_left;
            m_mouse_right_click = m_mouse_right;
            m_mouse_middle_click = m_mouse_middle;
            if (m_mouse_left || m_mouse_x != m_mouse_px) {
                DdkUnlock();
            }
            if (!m_mouse_left) {
                m_selected_slider_id = -1;
            }
            Cursor = m_credits_area.Contains(m_mousePosition) ? Cursors.Hand : Cursors.Default;
        }

        private bool DdkCalcFrame() {
            DrawScreen();
            return true;
        }

        private int Rnd(int n) {
            return m_rand.Next(n + 1);
        }

        private float Frnd(float range) {
            return (float)m_rand.NextDouble() * range;
        }

        private int HexToArgb(uint hexValue) {
            return unchecked((int)0xFF000000) | ((int)hexValue & 0xFFFFFF);
        }
    }
}