using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Reflection;

namespace cs.sfxr
{
    public partial class Sfxr : Form
    {

        /* ::::::::: Fields :::::::: */

        private int m_draw_count = 0;
        private bool m_first_frame = true;
        private int m_refresh_counter = 0;
        private bool m_drag_on_left_click = false;
        private bool m_dialog_opened = false;
        private Rectangle m_view_port;

        /* ::::::::: Methods :::::::: */

        private bool MouseInBox(int x, int y, int w, int h, Point p) {
            return p.X >= x && p.X < x + w && p.Y >= y && p.Y < y + h;
        }

        private bool MouseInBox(int x, int y, int w, int h) {
            return m_mouse_x >= x && m_mouse_x < x + w && m_mouse_y >= y && m_mouse_y < y + h;
        }

        private bool LoadTGA(ref Spriteset tiles, string filename) {
            string resource_name = $"{typeof(Sfxr).Namespace}.res.{filename}";
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream resourceStream = assembly.GetManifestResourceStream(resource_name)) {
                BinaryReader reader = new BinaryReader(resourceStream);
                byte id_length = reader.ReadByte();
                byte[] crap = reader.ReadBytes(11);
                int width = reader.ReadUInt16();
                int height = reader.ReadUInt16();
                byte bits = reader.ReadByte();
                int channels = bits / 8;
                byte image_descriptor_byte = reader.ReadByte();
                for (int i = 0; i < id_length; i++) {
                    reader.ReadByte();
                }
                tiles.Data = new int[width * height * sizeof(int)];
                for (int y = height - 1; y >= 0; y--) {
                    for (int x = 0; x < width; x++) {
                        /* 
                         * // Could be better
                         * uint pixel = 0;
                         * byte byte1 = reader.ReadByte();
                         * pixel |= byte1;
                         * byte byte2 = reader.ReadByte();
                         * pixel |= (uint)(byte2 << 8);
                         * byte byte3 = reader.ReadByte();
                         * pixel |= (uint)(byte3 << 16);
                         * tiles.data[y * width + x] = pixel;
                        */
                        // Now it is better
                        int pixel = reader.ReadByte();
                        pixel |= (int)(reader.ReadByte() << 8);
                        pixel |= (int)(reader.ReadByte() << 16);
                        tiles.Data[y * width + x] = pixel;
                    }
                }
                tiles.Height = height;
                tiles.Width = height;
                tiles.Pitch = width;
            }
            return false;
        }
        string NewFile(string forcedExtension) {
            string fileName = $"{Guid.NewGuid()}{forcedExtension}";
            return fileName;
        }

        private void LoadSound() {
            if (m_dialog_opened) {
                return;
            }
            m_dialog_opened = true;
            using (OpenFileDialog open_file_dialog = new OpenFileDialog()) {
                open_file_dialog.Filter = "SFX Sample Files (*.sfs)|*.sfs;|All Files (*.*)|*.*";
                open_file_dialog.FilterIndex = 1;
                open_file_dialog.RestoreDirectory = true;
                open_file_dialog.AddExtension = true;
                if (open_file_dialog.ShowDialog() == DialogResult.OK) {
                    var selectedFile = open_file_dialog.FileName;
                    ResetParams();
                    LoadSettings(selectedFile);
                    PlaySample();
                    m_dialog_opened = false;
                }
                else { m_dialog_opened = false; }
            }
        }

        private bool SaveFile(string file_name, string file_filter, out string file_path) {
            file_path = string.Empty;
            if (m_dialog_opened) {
                return false;
            }
            m_dialog_opened = true;
            using (SaveFileDialog save_file_dialog = new SaveFileDialog()) {
                save_file_dialog.FileName = file_name;
                save_file_dialog.Filter = $"{file_filter}|All Files (*.*)|*.*";
                save_file_dialog.FilterIndex = 1;
                save_file_dialog.RestoreDirectory = true;
                save_file_dialog.AddExtension = true;
                if (save_file_dialog.ShowDialog() == DialogResult.OK) {
                    var selected_file = save_file_dialog.FileName;
                    file_path = selected_file;
                    m_dialog_opened = false;
                    return true;
                }
                else {
                    m_dialog_opened = false;
                    return false;
                }
            }
        }

        /* ::::::::: Drawing :::::::: */

        private void ClearScreen(uint color) {
            for (int y = 0; y < m_height; y++) {
                for (int x = 0; x < m_width; x++) {
                    int index = y * m_width + x;
                    if (index >= 0 && index < m_ddkscreen32.Length) {
                        m_ddkscreen32[index] = HexToArgb(color);
                    }
                }
            }
        }

        private void DrawSprite(Spriteset sprites, int sx, int sy, int i, uint color) {
            for (int y = 0; y < sprites.Height; y++) {
                int offset = (sy + y) * m_width + sx;
                int spoffset = y * sprites.Pitch + i * sprites.Width;
                for (int x = 0; x < sprites.Width; x++) {
                    int p = sprites.Data[spoffset + x];
                    if (p != 0x300030) {
                        m_ddkscreen32[offset + x] = (color & 0xFF000000) != 0 ? p : HexToArgb(color);
                    }
                }
            }
        }

        private void DrawText(Spriteset font, int sx, int sy, uint color, string text) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }
            char[] string2 = text.ToCharArray();
            int len = string2.Length;
            for (int i = 0; i < len; i++) {
                DrawSprite(font, sx + i * 8, sy, string2[i] - ' ', color);
            }
        }

        private void DrawBar(int sx, int sy, int w, int h, uint c) {
            int color = HexToArgb(c);
            for (int y = sy; y < sy + h; y++) {
                int offset = y * m_width + sx;
                int x1 = 0;
                if (w > 8) {
                    for (x1 = 0; x1 < w - 8; x1 += 8) {
                        for (int i = 0; i < 8; i++) {
                            m_ddkscreen32[offset++] = color;
                        }
                    }
                }
                for (int x = x1; x < w; x++) {
                    m_ddkscreen32[offset++] = color;
                }
            }
        }

        private bool Slider(int x, int y, ref F value, bool bipolar, string text, int identifier) {
            bool result = false;
            bool mouseInBox = MouseInBox(x - 50, y, 200, 10);
            bool isSelectedSlider = m_selected_slider_id == identifier;
            if (mouseInBox || isSelectedSlider) {
                if (m_selected_slider_id == -1) {
                    m_selected_slider_id = identifier;
                }
                if (isSelectedSlider) {
                    if (m_mouse_right_click) {
                        value = 0.0f;
                        result = true;
                    }
                    else if (m_mouse_left_click) {
                        if (m_drag_on_left_click) {
                            m_vselected = value;
                        }
                        else {
                            value = bipolar ? (m_mouse_x - x) / 50.0f - 1.0f : (m_mouse_x - x) / 100.0f;
                            result = true;
                        }
                    }
                }
            }
            float mv = m_vselected != value ? 0.0f : m_mouse_x - m_mouse_px;
            if (bipolar) {
                value += mv * 0.005f;
                value = Math.Max(-1.0f, Math.Min(1.0f, value));
            }
            else {
                value += mv * 0.0025f;
                value = Math.Max(0.0f, Math.Min(1.0f, value));
            }
            int ival = bipolar ? (int)(value * 49.5f + 49.5f) : (int)(value * 99);
            DrawBar(x - 1, y, 102, 10, 0x000000);
            DrawBar(x, y + 1, ival, 8, 0xF0C090);
            DrawBar(x + ival, y + 1, 100 - ival, 8, 0x807060);
            DrawBar(x + ival, y + 1, 1, 8, 0xFFFFFF);
            if (bipolar) {
                DrawBar(x + 50, y - 1, 1, 3, 0x000000);
                DrawBar(x + 50, y + 8, 1, 3, 0x000000);
            }
            uint tcol = m_wave_type > 0 && (value == p_duty || value == p_duty_ramp) ? (uint)0x808080 : 0x000000;
            DrawText(m_font, x - 4 - text.Length * 8, y + 1, tcol, text);
            return result;
        }

        private bool Button(int x, int y, bool highlight, string text, int id) {
            uint color1 = 0x000000;
            uint color2 = highlight ? (uint)0x988070 : 0xA09088;
            uint color3 = highlight ? (uint)0xFFF0E0 : 0x000000;
            bool hover = MouseInBox(x, y, 100, 17);
            if (hover && m_mouse_left_click) {
                m_vcurbutton = id;
            }
            bool current = (m_vcurbutton == id);
            if (current && hover) {
                color1 = 0xA09088;
                color2 = 0xFFF0E0;
                color3 = 0xA09088;
            }
            DrawBar(x - 1, y - 1, 102, 19, color1);
            DrawBar(x, y, 100, 17, color2);
            DrawText(m_font, x + 5, y + 5, color3, text);
            return current && hover && !m_mouse_left;
        }

        private bool ButtonWH(int x, int y, int w, int h, bool highlight, string text, int id) {
            uint color1 = 0x000000;
            uint color2 = highlight ? (uint)0x988070 : 0xA09088;
            uint color3 = highlight ? (uint)0xFFF0E0 : 0x000000;
            bool hover = MouseInBox(x, y, w, h);
            if (hover && m_mouse_left_click) {
                m_vcurbutton = id;
            }
            bool current = (m_vcurbutton == id);
            if (current && hover) {
                color1 = 0xA09088;
                color2 = 0xFFF0E0;
                color3 = 0xA09088;
            }
            DrawBar(x - 1, y - 1, w + 2, h + 2, color1);
            DrawBar(x, y, w, h, color2);
            DrawText(m_font, x + 5, y + 5, color3, text);
            return current && hover && !m_mouse_left;
        }

        private void DrawScreen() {
            bool redraw = true;
            if (!m_first_frame && m_mouse_x - m_mouse_px == 0 && m_mouse_y - m_mouse_py == 0
                && !m_mouse_left && !m_mouse_right) {
                redraw = false;
            }
            if (!m_mouse_left) {
                if (m_vselected != null || m_vcurbutton > -1) {
                    redraw = true;
                    m_refresh_counter = 2;
                }
                m_vselected = null;
            }
            if (m_refresh_counter > 0) {
                m_refresh_counter--;
                redraw = true;
            }
            if (m_playing_sample) {
                redraw = true;
            }
            if (m_draw_count++ > 20) {
                redraw = true;
                m_draw_count = 0;
            }
            if (!redraw) {
                return;
            }
            m_first_frame = false;
            DdkLock();
            ClearScreen(0xC0B090);
            DrawText(m_font, m_width - 110, 10, 0x504030, "BY TPBELDIE");
            DrawText(m_font, 10, 10, 0x504030, "GENERATOR");
            for (int i = 0; i < 7; i++) {
                if (Button(5, 35 + i * 30, false, m_categories[i].Name, 300 + i)) {
                    switch (i) {
                        case 0: // Pickup/coin.
                            {
                                ResetParams();
                                p_base_freq = 0.4f + Frnd(0.5f);
                                p_env_attack = 0.0f;
                                p_env_sustain = Frnd(0.1f);
                                p_env_decay = 0.1f + Frnd(0.4f);
                                p_env_punch = 0.3f + Frnd(0.3f);
                                if (Rnd(1) > 0) {
                                    p_arp_speed = 0.5f + Frnd(0.2f);
                                    p_arp_mod = 0.2f + Frnd(0.4f);
                                }
                                break;
                            }
                        case 1: // Laser/shoot.
                            {
                                ResetParams();
                                m_wave_type = Rnd(2);
                                if (m_wave_type == 2 && Rnd(1) > 0) {
                                    m_wave_type = Rnd(1);
                                }
                                p_base_freq = 0.5f + Frnd(0.5f);
                                p_freq_limit = p_base_freq - 0.2f - Frnd(0.6f);
                                if (p_freq_limit < 0.2f) {
                                    p_freq_limit = 0.2f;
                                }
                                p_freq_ramp = -0.15f - Frnd(0.2f);
                                if (Rnd(2) == 0) {
                                    p_base_freq = 0.3f + Frnd(0.6f);
                                    p_freq_limit = Frnd(0.1f);
                                    p_freq_ramp = -0.35f - Frnd(0.3f);
                                }
                                if (Rnd(1) > 0) {
                                    p_duty = Frnd(0.5f);
                                    p_duty_ramp = Frnd(0.2f);
                                }
                                else {
                                    p_duty = 0.4f + Frnd(0.5f);
                                    p_duty_ramp = -Frnd(0.7f);
                                }
                                p_env_attack = 0.0f;
                                p_env_sustain = 0.1f + Frnd(0.2f);
                                p_env_decay = Frnd(0.4f);
                                if (Rnd(1) > 0) {
                                    p_env_punch = Frnd(0.3f);
                                }
                                if (Rnd(2) == 0) {
                                    p_pha_offset = Frnd(0.2f);
                                    p_pha_ramp = -Frnd(0.2f);
                                }
                                if (Rnd(1) > 0) {
                                    p_hpf_freq = Frnd(0.3f);
                                }
                                break;
                            }
                        case 2: // Explosion.
                            {
                                ResetParams();
                                m_wave_type = 3;
                                if (Rnd(1) > 0) {
                                    p_base_freq = 0.1f + Frnd(0.4f);
                                    p_freq_ramp = -0.1f + Frnd(0.4f);
                                }
                                else {
                                    p_base_freq = 0.2f + Frnd(0.7f);
                                    p_freq_ramp = -0.2f - Frnd(0.2f);
                                }
                                p_base_freq *= p_base_freq;
                                if (Rnd(4) == 0) {
                                    p_freq_ramp = 0.0f;
                                }
                                if (Rnd(2) == 0) {
                                    p_repeat_speed = 0.3f + Frnd(0.5f);
                                }
                                p_env_attack = 0.0f;
                                p_env_sustain = 0.1f + Frnd(0.3f);
                                p_env_decay = Frnd(0.5f);
                                if (Rnd(1) == 0) {
                                    p_pha_offset = -0.3f + Frnd(0.9f);
                                    p_pha_ramp = -Frnd(0.3f);
                                }
                                p_env_punch = 0.2f + Frnd(0.6f);
                                if (Rnd(1) > 0) {
                                    p_vib_strength = Frnd(0.7f);
                                    p_vib_speed = Frnd(0.6f);
                                }
                                if (Rnd(2) == 0) {
                                    p_arp_speed = 0.6f + Frnd(0.3f);
                                    p_arp_mod = 0.8f - Frnd(1.6f);
                                }
                                break;
                            }
                        case 3: // Powerup.
                            {
                                ResetParams();
                                if (Rnd(1) > 0) {
                                    m_wave_type = 1;
                                }
                                else {
                                    p_duty = Frnd(0.6f);
                                }
                                if (Rnd(1) > 0) {
                                    p_base_freq = 0.2f + Frnd(0.3f);
                                    p_freq_ramp = 0.1f + Frnd(0.4f);
                                    p_repeat_speed = 0.4f + Frnd(0.4f);
                                }
                                else {
                                    p_base_freq = 0.2f + Frnd(0.3f);
                                    p_freq_ramp = 0.05f + Frnd(0.2f);
                                    if (Rnd(1) > 0) {
                                        p_vib_strength = Frnd(0.7f);
                                        p_vib_speed = Frnd(0.6f);
                                    }
                                }
                                p_env_attack = 0.0f;
                                p_env_sustain = Frnd(0.4f);
                                p_env_decay = 0.1f + Frnd(0.4f);
                                break;
                            }
                        case 4: // Hit/hurt.
                            {
                                ResetParams();
                                m_wave_type = Rnd(2);
                                if (m_wave_type == 2) {
                                    m_wave_type = 3;
                                }
                                if (m_wave_type == 0) {
                                    p_duty = Frnd(0.6f);
                                }
                                p_base_freq = 0.2f + Frnd(0.6f);
                                p_freq_ramp = -0.3f - Frnd(0.4f);
                                p_env_attack = 0.0f;
                                p_env_sustain = Frnd(0.1f);
                                p_env_decay = 0.1f + Frnd(0.2f);
                                if (Rnd(1) > 0) {
                                    p_hpf_freq = Frnd(0.3f);
                                }
                                break;
                            }
                        case 5: // Jump.
                            {
                                ResetParams();
                                m_wave_type = 0;
                                p_duty = Frnd(0.6f);
                                p_base_freq = 0.3f + Frnd(0.3f);
                                p_freq_ramp = 0.1f + Frnd(0.2f);
                                p_env_attack = 0.0f;
                                p_env_sustain = 0.1f + Frnd(0.3f);
                                p_env_decay = 0.1f + Frnd(0.2f);
                                if (Rnd(1) > 0) {
                                    p_hpf_freq = Frnd(0.3f);
                                }
                                if (Rnd(1) > 0) {
                                    p_lpf_freq = 1.0f - Frnd(0.6f);
                                }
                                break;
                            }
                        case 6: // Blip/select.
                            {
                                ResetParams();
                                m_wave_type = Rnd(1);
                                if (m_wave_type == 0) {
                                    p_duty = Frnd(0.6f);
                                }
                                p_base_freq = 0.2f + Frnd(0.4f);
                                p_env_attack = 0.0f;
                                p_env_sustain = 0.1f + Frnd(0.1f);
                                p_env_decay = Frnd(0.2f);
                                p_hpf_freq = 0.1f;
                                break;
                            }
                        default: {
                                break;
                            }
                    }
                    PlaySample();
                }
            }
            DrawBar(110, 0, 2, 480, 0x000000);
            DrawText(m_font, 120, 10, 0x504030, "MANUAL SETTINGS");
            DrawSprite(m_ld48, 8, 440, 0, 0xB0A080);
            bool do_play = false;
            if (Button(130, 30, m_wave_type == 0, "SQUAREWAVE", 10)) {
                m_wave_type = 0;
                do_play = true;
            }
            if (Button(250, 30, m_wave_type == 1, "SAWTOOTH", 11)) {
                m_wave_type = 1;
                do_play = true;
            }
            if (Button(370, 30, m_wave_type == 2, "SINEWAVE", 12)) {
                m_wave_type = 2;
                do_play = true;
            }
            if (Button(490, 30, m_wave_type == 3, "NOISE", 13)) {
                m_wave_type = 3;
                do_play = true;
            }
            /* Drag Toggle
            if (m_drag_on_left_click) {
                if (ButtonWH(490, 140, 17, 17, m_drag_on_left_click, "X", 101)) {
                    m_drag_on_left_click = !m_drag_on_left_click;
                }
            }
            else {
                if (ButtonWH(490, 140, 17, 17, m_drag_on_left_click, "", 101)) {
                    m_drag_on_left_click = !m_drag_on_left_click;
                }
            }
            DrawText(m_font, 515, 145, 0x000000, "DRAG BARS");
            DrawBar(5 - 1 - 1, 412 - 1 - 1, 102 + 2, 19 + 2, 0x000000);
            */
            if (Button(5, 412, false, "RANDOMIZE", 40)) {
                p_base_freq = (float)Math.Pow(Frnd(2.0f) - 1.0f, 2.0f);
                if (Rnd(1) > 0) {
                    p_base_freq = (float)Math.Pow(Frnd(2.0f) - 1.0f, 3.0f) + 0.5f;
                }
                p_freq_limit = 0.0f;
                p_freq_ramp = (float)Math.Pow(Frnd(2.0f) - 1.0f, 5.0f);
                if (p_base_freq > 0.7f && p_freq_ramp > 0.2f) {
                    p_freq_ramp = -p_freq_ramp;
                }
                if (p_base_freq < 0.2f && p_freq_ramp < -0.05f) {
                    p_freq_ramp = -p_freq_ramp;
                }
                p_freq_dramp = (float)Math.Pow(Frnd(2.0f) - 1.0f, 3.0f);
                p_duty = Frnd(2.0f) - 1.0f;
                p_duty_ramp = (float)Math.Pow(Frnd(2.0f) - 1.0f, 3.0f);
                p_vib_strength = (float)Math.Pow(Frnd(2.0f) - 1.0f, 3.0f);
                p_vib_speed = Frnd(2.0f) - 1.0f;
                p_vib_delay = Frnd(2.0f) - 1.0f;
                p_env_attack = (float)Math.Pow(Frnd(2.0f) - 1.0f, 3.0f);
                p_env_sustain = (float)Math.Pow(Frnd(2.0f) - 1.0f, 2.0f);
                p_env_decay = Frnd(2.0f) - 1.0f;
                p_env_punch = (float)Math.Pow(Frnd(0.8f), 2.0f);
                if (p_env_attack + p_env_sustain + p_env_decay < 0.2f) {
                    p_env_sustain += 0.2f + Frnd(0.3f);
                    p_env_decay += 0.2f + Frnd(0.3f);
                }
                p_lpf_resonance = Frnd(2.0f) - 1.0f;
                p_lpf_freq = 1.0f - (float)Math.Pow(Frnd(1.0f), 3.0f);
                p_lpf_ramp = (float)Math.Pow(Frnd(2.0f) - 1.0f, 3.0f);
                if (p_lpf_freq < 0.1f && p_lpf_ramp < -0.05f) {
                    p_lpf_ramp = -p_lpf_ramp;
                }
                p_hpf_freq = (float)Math.Pow(Frnd(1.0f), 5.0f);
                p_hpf_ramp = (float)Math.Pow(Frnd(2.0f) - 1.0f, 5.0f);
                p_pha_offset = (float)Math.Pow(Frnd(2.0f) - 1.0f, 3.0f);
                p_pha_ramp = (float)Math.Pow(Frnd(2.0f) - 1.0f, 3.0f);
                p_repeat_speed = Frnd(2.0f) - 1.0f;
                p_arp_speed = Frnd(2.0f) - 1.0f;
                p_arp_mod = Frnd(2.0f) - 1.0f;
                do_play = true;
            }
            if (Button(5, 382, false, "MUTATE", 30)) {
                if (Rnd(1) > 0) { p_base_freq += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_freq_ramp += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_freq_dramp += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_duty += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_duty_ramp += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_vib_strength += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_vib_speed += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_vib_delay += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_env_attack += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_env_sustain += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_env_decay += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_env_punch += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_lpf_resonance += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_lpf_freq += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_lpf_ramp += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_hpf_freq += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_hpf_ramp += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_pha_offset += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_pha_ramp += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_repeat_speed += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_arp_speed += Frnd(0.1f) - 0.05f; }
                if (Rnd(1) > 0) { p_arp_mod += Frnd(0.1f) - 0.05f; }
                do_play = true;
            }
            DrawText(m_font, 515, 170, 0x000000, "VOLUME");
            DrawBar(490 - 1 - 1 + 60, 180 - 1 + 5, 70, 2, 0x000000);
            DrawBar(490 - 1 - 1 + 60 + 68, 180 - 1 + 5, 2, 205, 0x000000);
            DrawBar(490 - 1 - 1 + 60, 180 - 1, 42 + 2, 10 + 2, 0xFF0000);
            int sliderId = 0;
            if (Slider(490, 180, ref m_sound_vol, false, " ", sliderId++)) {
                PlaySample();
            }
            if (Button(490, 200, false, "PLAY SOUND", 20)) {
                PlaySample();
            }
            if (Button(490, 290, false, "LOAD SOUND", 14)) {
                LoadSound();
            }
            if (Button(490, 320, false, "SAVE SOUND", 15)) {
                string filename = NewFile(".sfs");
                if (SaveFile(filename, "SFX Sample Files (*.sfs)|*.sfs;", out string file_path)) {
                    SaveSettings(file_path);
                }
            }
            DrawBar(490 - 1 - 1 + 60, 380 - 1 + 9, 70, 2, 0x000000);
            DrawBar(490 - 1 - 2, 380 - 1 - 2, 102 + 4, 19 + 4, 0x000000);
            if (Button(490, 380, false, "EXPORT .WAV", 16)) {
                string filename = NewFile(".wav");
                if (SaveFile(filename, "Waveform Audio Files (*.wav)|*.wav;", out string file_path)) {
                    WriteWaveFormFile(file_path);
                }
            }
            string str = $"{m_wav_freq} HZ";
            if (Button(490, 410, false, str, 18)) {
                m_wav_freq = m_wav_freq == 44100 ? 22050 : 44100;
            }
            str = $"{m_wav_bits}-BIT";
            if (Button(490, 440, false, str, 19)) {
                m_wav_bits = m_wav_bits == 16 ? 8 : 16;
            }
            int ypos = 4;
            int xpos = 350;
            DrawBar(xpos - 190, ypos * 18 - 5, 300, 2, 0x0000000);
            Slider(xpos, ypos++ * 18, ref p_env_attack, false, "ATTACK TIME", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_env_sustain, false, "SUSTAIN TIME", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_env_punch, false, "SUSTAIN PUNCH", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_env_decay, false, "DECAY TIME", sliderId++);
            DrawBar(xpos - 190, ypos * 18 - 5, 300, 2, 0x0000000);
            Slider(xpos, ypos++ * 18, ref p_base_freq, false, "START FREQUENCY", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_freq_limit, false, "MIN FREQUENCY", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_freq_ramp, true, "SLIDE", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_freq_dramp, true, "DELTA SLIDE", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_vib_strength, false, "VIBRATO DEPTH", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_vib_speed, false, "VIBRATO SPEED", sliderId++);
            DrawBar(xpos - 190, ypos * 18 - 5, 300, 2, 0x0000000);
            Slider(xpos, ypos++ * 18, ref p_arp_mod, true, "CHANGE AMOUNT", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_arp_speed, false, "CHANGE SPEED", sliderId++);
            DrawBar(xpos - 190, ypos * 18 - 5, 300, 2, 0x0000000);
            Slider(xpos, ypos++ * 18, ref p_duty, false, "SQUARE DUTY", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_duty_ramp, true, "DUTY SWEEP", sliderId++);
            DrawBar(xpos - 190, ypos * 18 - 5, 300, 2, 0x0000000);
            Slider(xpos, ypos++ * 18, ref p_repeat_speed, false, "REPEAT SPEED", sliderId++);
            DrawBar(xpos - 190, ypos * 18 - 5, 300, 2, 0x0000000);
            Slider(xpos, ypos++ * 18, ref p_pha_offset, true, "PHASER OFFSET", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_pha_ramp, true, "PHASER SWEEP", sliderId++);
            DrawBar(xpos - 190, ypos * 18 - 5, 300, 2, 0x0000000);
            Slider(xpos, ypos++ * 18, ref p_lpf_freq, false, "LP FILTER CUTOFF", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_lpf_ramp, true, "LP FILTER CUTOFF SWEEP", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_lpf_resonance, false, "LP FILTER RESONANCE", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_hpf_freq, false, "HP FILTER CUTOFF", sliderId++);
            Slider(xpos, ypos++ * 18, ref p_hpf_ramp, true, "HP FILTER CUTOFF SWEEP", sliderId++);
            DrawBar(xpos - 190, ypos * 18 - 5, 300, 2, 0x0000000);
            DrawBar(xpos - 190, 4 * 18 - 5, 1, (ypos - 4) * 18, 0x0000000);
            DrawBar(xpos - 190 + 299, 4 * 18 - 5, 1, (ypos - 4) * 18, 0x0000000);
            if (do_play) { PlaySample(); }
            DdkUnlock();
            if (!m_mouse_left) { m_vcurbutton = -1; }
        }

        private void DdkLock() {
            BitmapData bmpData = m_bitmap.LockBits(m_view_port, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(m_ddkscreen32, 0, bmpData.Scan0, m_ddkscreen32.Length);
            m_bitmap.UnlockBits(bmpData);
        }

        private void DdkUnlock() {
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            e.Graphics.DrawImage(m_bitmap, 0, 0);
        }
    }
}
