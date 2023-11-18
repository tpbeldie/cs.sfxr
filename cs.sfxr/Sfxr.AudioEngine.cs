using System;
using System.IO;
using System.Text;
using System.Media;
using System.Threading;

namespace cs.sfxr
{
    public partial class Sfxr
    {

        private void InitializeAudio() {
            m_soundPlayer = new SoundPlayer();
            // Start a background thread for audio playback.
            m_audio_thread = new Thread(AudioThread);
            m_audio_thread.IsBackground = true;
            m_audio_thread.Start();
        }

        private void AudioThread() {
            while (true) {
                if (m_playing_sample && !m_mute_stream) {
                    byte[] buffer = GenerateWavData();
                    m_soundPlayer.Stream?.Dispose();
                    m_soundPlayer.Stream = new MemoryStream(buffer);
                    m_soundPlayer.Play();
                }
                Thread.Sleep(100);
            }
        }

        public byte[] GenerateWavData() {
            using (MemoryStream stream = new MemoryStream()) {
                WriteWaveHeader(stream);
                // Write sample data
                m_mute_stream = true;
                m_file_samples_written = 0;
                m_file_sample = 0.0f;
                m_fileacc = 0;
                PlaySample();
                while (m_playing_sample) {
                    SynthSample(256, null, stream);
                }
                m_mute_stream = false;
                // Seek back to header and write size info
                stream.Seek(4, SeekOrigin.Begin);
                Write(stream, (int)(stream.Length - 8));
                stream.Seek(40, SeekOrigin.Begin);
                // I've got no f-ing idea where the magic number 18 came from, but it works. :))
                Write(stream, (int)(m_file_samples_written * m_wav_bits / (m_wav_freq == 44100 ? 8 : 18)));
                return stream.ToArray();
            }
        }


        private void WriteWaveHeader(Stream stream) {
            // "RIFF"
            Write(stream, new char[] { 'R', 'I', 'F', 'F' });
            // Remaining file size (to be filled later)
            Write(stream, 0);
            // "WAVE"
            Write(stream, new char[] { 'W', 'A', 'V', 'E' });
            // "fmt "
            Write(stream, new char[] { 'f', 'm', 't', ' ' });
            // Chunk size
            Write(stream, 16);
            // Compression code
            Write(stream, (short)1);
            // Channels
            Write(stream, (short)1);
            // Sample Rate
            Write(stream, m_wav_freq);
            // Bytes/sec
            Write(stream, m_wav_freq * m_wav_bits / 8);
            // Block align
            Write(stream, (short)(m_wav_bits / 8));
            // Bits per sample
            Write(stream, (short)m_wav_bits);
            // "data"
            Write(stream, new char[] { 'd', 'a', 't', 'a' });
            // Chunk size (to be filled later)
            Write(stream, 0);
        }

        private void Write(Stream stream, char[] chars) {
            byte[] bytes = Encoding.UTF8.GetBytes(chars);
            stream.Write(bytes, 0, bytes.Length);
        }
        private void Write(Stream stream, int value) {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private void Write(Stream stream, short value) {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private void Write(byte[] buffer, char[] chars) {
            Encoding.ASCII.GetBytes(chars, 0, chars.Length, buffer, 0);
        }

        private void Write(byte[] buffer, int value) {
            BitConverter.GetBytes(value).CopyTo(buffer, 0);
        }

        private void Write(byte[] buffer, short value, int index) {
            BitConverter.GetBytes(value).CopyTo(buffer, index);
        }

        private void PlaySample() {
            ResetSample(false);
            m_playing_sample = true;
        }

        private void SynthSample(int length, float[] buffer, Stream file) {
            for (int i = 0; i < length; i++) {
                if (!m_playing_sample) {
                    break;
                }
                m_rep_time++;
                if (m_rep_limit != 0 && m_rep_time >= m_rep_limit) {
                    m_rep_time = 0;
                    ResetSample(true);
                }
                // Frequency envelopes/arpeggios
                m_arp_time++;
                if (m_arp_limit != 0 && m_arp_time >= m_arp_limit) {
                    m_arp_limit = 0;
                    m_fperiod *= m_arp_mod;
                }
                m_fslide += m_fdslide;
                m_fperiod *= m_fslide;
                if (m_fperiod > m_fmaxperiod) {
                    m_fperiod = m_fmaxperiod;
                    if (p_freq_limit > 0.0f) {
                        m_playing_sample = false;
                    }
                }
                float rfperiod = (float)m_fperiod;
                if (m_vib_amp > 0.0f) {
                    m_vib_phase += m_vib_speed;
                    rfperiod = (float)(m_fperiod * (1.0f + Math.Sin(m_vib_phase) * m_vib_amp));
                }
                m_period = (int)rfperiod;
                if (m_period < 8) {
                    m_period = 8;
                }
                m_square_duty += m_square_slide;
                if (m_square_duty < 0.0f) {
                    m_square_duty = 0.0f;
                }
                if (m_square_duty > 0.5f) {
                    m_square_duty = 0.5f;
                }
                // Volume envelope
                m_env_time++;
                if (m_env_time > m_env_length[m_env_stage]) {
                    m_env_time = 0;
                    m_env_stage++;
                    if (m_env_stage == 3) {
                        m_playing_sample = false;
                    }
                }
                if (m_env_stage == 0) {
                    m_env_vol = (float)m_env_time / m_env_length[0];
                }
                if (m_env_stage == 1) {
                    m_env_vol = 1.0f + (float)Math.Pow(1.0f - (float)m_env_time / m_env_length[1], 1.0f) * 2.0f * p_env_punch;
                }
                if (m_env_stage == 2) {
                    m_env_vol = 1.0f - (float)m_env_time / m_env_length[2];
                }
                // Phaser step
                m_fphase += m_fdphase;
                m_iphase = Math.Abs((int)m_fphase);
                if (m_iphase > 1023) {
                    m_iphase = 1023;
                }
                if (m_flthp_d != 0.0f) {
                    m_flthp *= m_flthp_d;
                    if (m_flthp < 0.00001f) {
                        m_flthp = 0.00001f;
                    }
                    if (m_flthp > 0.1f) {
                        m_flthp = 0.1f;
                    }
                }
                float ssample = 0.0f;
                for (int si = 0; si < 8; si++) // 8x supersampling.
                {
                    float sample = 0.0f;
                    m_phase++;
                    if (m_phase >= m_period) {
                        m_phase %= m_period;
                        if (m_wave_type == 3) {
                            for (int j = 0; j < 32; j++) {
                                m_noise_buffer[j] = Frnd(2.0f) - 1.0f;
                            }
                        }
                    }
                    // Base waveform
                    float fp = (float)m_phase / m_period;
                    switch (m_wave_type) {
                        case 0: // Square
                            sample = fp < m_square_duty ? 0.5f : -0.5f;
                            break;
                        case 1: // Sawtooth
                            sample = 1.0f - fp * 2;
                            break;
                        case 2: // Sine
                            sample = (float)Math.Sin(fp * 2 * Math.PI);
                            break;
                        case 3: // Noise
                            sample = m_noise_buffer[m_phase * 32 / m_period];
                            break;
                    }
                    // LP filter
                    float pp = m_fltp;
                    m_fltw *= m_fltw_d;
                    if (m_fltw < 0.0f) {
                        m_fltw = 0.0f;
                    }
                    if (m_fltw > 0.1f) {
                        m_fltw = 0.1f;
                    }
                    if (p_lpf_freq != 1.0f) {
                        m_fltdp += (sample - m_fltp) * m_fltw;
                        m_fltdp -= m_fltdp * m_fltdmp;
                    }
                    else {
                        m_fltp = sample;
                        m_fltdp = 0.0f;
                    }
                    m_fltp += m_fltdp;
                    // HP filter
                    m_fltphp += m_fltp - pp;
                    m_fltphp -= m_fltphp * m_flthp;
                    sample = m_fltphp;
                    // Phaser
                    m_phaser_buffer[m_ipp & 1023] = sample;
                    sample += m_phaser_buffer[(m_ipp - m_iphase + 1024) & 1023];
                    m_ipp = (m_ipp + 1) & 1023;
                    // Final accumulation and envelope application
                    ssample += sample * m_env_vol;
                }
                ssample = ssample / 8 * m_master_vol;
                ssample *= 2.0f * m_sound_vol;
                if (buffer != null) {
                    if (ssample > 1.0f) {
                        ssample = 1.0f;
                    }
                    if (ssample < -1.0f) {
                        ssample = -1.0f;
                    }
                    buffer[i] = ssample;
                }
                if (file != null) {
                    // Quantize depending on format
                    // Accumulate/count to accommodate variable sample rate?
                    // Arbitrary gain to get reasonable output volume...
                    ssample *= 4.0f;
                    if (ssample > 1.0f) {
                        ssample = 1.0f;
                    }
                    if (ssample < -1.0f) {
                        ssample = -1.0f;
                    }
                    m_file_sample += ssample;
                    m_fileacc++;
                    if (m_wav_freq == 44100 || m_fileacc == 2) {
                        m_file_sample /= m_fileacc;
                        m_fileacc = 0;
                        if (m_wav_bits == 16) {
                            short isample = (short)(m_file_sample * 32000);
                            byte[] bytes = BitConverter.GetBytes(isample);
                            file.Write(bytes, 0, 2);
                        }
                        else {
                            byte isample = (byte)(m_file_sample * 127 + 128);
                            file.WriteByte(isample);
                        }
                        m_file_sample = 0.0f;
                    }
                    m_file_samples_written++;
                }
            }
        }

        private bool WriteWaveFormFile(string filename) {
            byte[] buffer = GenerateWavData();
            using (FileStream stream = new FileStream(filename, FileMode.Create, FileAccess.Write)) {
                stream.Write(buffer, 0, buffer.Length);
                stream.Close();
            }
            return true;
        }

        private void ResetSample(bool restart) {
            if (!restart) {
                m_phase = 0;
            }
            m_fperiod = 100.0 / (p_base_freq * p_base_freq + 0.001);
            m_period = (int)m_fperiod;
            m_fmaxperiod = 100.0 / (p_freq_limit * p_freq_limit + 0.001);
            m_fslide = 1.0 - Math.Pow(p_freq_ramp, 3.0) * 0.01;
            m_fdslide = -Math.Pow(p_freq_dramp, 3.0) * 0.000001;
            m_square_duty = 0.5f - p_duty * 0.5f;
            m_square_slide = -p_duty_ramp * 0.00005f;
            if (p_arp_mod >= 0.0f) {
                m_arp_mod = 1.0 - Math.Pow(p_arp_mod, 2.0) * 0.9;
            }
            else {
                m_arp_mod = 1.0 + Math.Pow(p_arp_mod, 2.0) * 10.0;
            }
            m_arp_time = 0;
            m_arp_limit = (int)(Math.Pow(1.0f - p_arp_speed, 2.0f) * 20000 + 32);
            if (p_arp_speed == 1.0f) {
                m_arp_limit = 0;
            }
            if (!restart) {
                // Reset filter
                m_fltp = 0.0f;
                m_fltdp = 0.0f;
                m_fltw = (float)(Math.Pow(p_lpf_freq, 3.0f) * 0.1f);
                m_fltw_d = 1.0f + p_lpf_ramp * 0.0001f;
                m_fltdmp = (float)(5.0f / (1.0f + Math.Pow(p_lpf_resonance, 2.0f) * 20.0f) * (0.01f + m_fltw));
                if (m_fltdmp > 0.8f) {
                    m_fltdmp = 0.8f;
                }
                m_fltphp = 0.0f;
                m_flthp = (float)(Math.Pow(p_hpf_freq, 2.0f) * 0.1f);
                m_flthp_d = 1.0f + p_hpf_ramp * 0.0003f;
                // Reset vibrato
                m_vib_phase = 0.0f;
                m_vib_speed = (float)Math.Pow(p_vib_speed, 2.0f) * 0.01f;
                m_vib_amp = p_vib_strength * 0.5f;
                // Reset envelope
                m_env_vol = 0.0f;
                m_env_stage = 0;
                m_env_time = 0;
                m_env_length[0] = (int)(p_env_attack * p_env_attack * 100000.0f);
                m_env_length[1] = (int)(p_env_sustain * p_env_sustain * 100000.0f);
                m_env_length[2] = (int)(p_env_decay * p_env_decay * 100000.0f);
                m_fphase = (float)Math.Pow(p_pha_offset, 2.0f) * 1020.0f;
                if (p_pha_offset < 0.0f) {
                    m_fphase = -m_fphase;
                }
                m_fdphase = (float)Math.Pow(p_pha_ramp, 2.0f) * 1.0f;
                if (p_pha_ramp < 0.0f) {
                    m_fdphase = -m_fdphase;
                }
                m_iphase = Math.Abs((int)m_fphase);
                m_ipp = 0;
                for (int i = 0; i < 1024; i++) {
                    m_phaser_buffer[i] = 0.0f;
                }
                for (int i = 0; i < 32; i++) {
                    m_noise_buffer[i] = Frnd(2.0f) - 1.0f;
                }
                m_rep_time = 0;
                m_rep_limit = (int)(Math.Pow(1.0f - p_repeat_speed, 2.0f) * 20000 + 32);
                if (p_repeat_speed == 0.0f) {
                    m_rep_limit = 0;
                }
            }
        }
        private void ResetParams() {
            m_wave_type = 0;
            p_base_freq = 0.3f;
            p_freq_limit = 0.0f;
            p_freq_ramp = 0.0f;
            p_freq_dramp = 0.0f;
            p_duty = 0.0f;
            p_duty_ramp = 0.0f;
            p_vib_strength = 0.0f;
            p_vib_speed = 0.0f;
            p_vib_delay = 0.0f;
            p_env_attack = 0.0f;
            p_env_sustain = 0.3f;
            p_env_decay = 0.4f;
            p_env_punch = 0.0f;
            m_filter_on = false;
            p_lpf_resonance = 0.0f;
            p_lpf_freq = 1.0f;
            p_lpf_ramp = 0.0f;
            p_hpf_freq = 0.0f;
            p_hpf_ramp = 0.0f;
            p_pha_offset = 0.0f;
            p_pha_ramp = 0.0f;
            p_repeat_speed = 0.0f;
            p_arp_speed = 0.0f;
            p_arp_mod = 0.0f;
        }

        private bool LoadSettings(string filename) {
            using (FileStream file = new FileStream(filename, FileMode.Open, FileAccess.Read)) {
                using (BinaryReader reader = new BinaryReader(file)) {
                    int version = reader.ReadInt32();
                    if (version != 100 && version != 101 && version != 102) {
                        return false;
                    }
                    m_wave_type = reader.ReadInt32();
                    m_sound_vol = version == 102 ? reader.ReadSingle() : 0.5f;
                    p_base_freq = reader.ReadSingle();
                    p_freq_limit = reader.ReadSingle();
                    p_freq_ramp = reader.ReadSingle();
                    if (version >= 101) {
                        p_freq_dramp = reader.ReadSingle();
                    }
                    p_duty = reader.ReadSingle();
                    p_duty_ramp = reader.ReadSingle();
                    p_vib_strength = reader.ReadSingle();
                    p_vib_speed = reader.ReadSingle();
                    p_vib_delay = reader.ReadSingle();
                    p_env_attack = reader.ReadSingle();
                    p_env_sustain = reader.ReadSingle();
                    p_env_decay = reader.ReadSingle();
                    p_env_punch = reader.ReadSingle();
                    m_filter_on = reader.ReadBoolean();
                    p_lpf_resonance = reader.ReadSingle();
                    p_lpf_freq = reader.ReadSingle();
                    p_lpf_ramp = reader.ReadSingle();
                    p_hpf_freq = reader.ReadSingle();
                    p_hpf_ramp = reader.ReadSingle();
                    p_pha_offset = reader.ReadSingle();
                    p_pha_ramp = reader.ReadSingle();
                    p_repeat_speed = reader.ReadSingle();
                    if (version >= 101) {
                        p_arp_speed = reader.ReadSingle();
                        p_arp_mod = reader.ReadSingle();
                    }
                    return true;
                }
            }
        }

        private bool SaveSettings(string filename) {
            using (FileStream file = new FileStream(filename, FileMode.Create, FileAccess.Write)) {
                using (BinaryWriter writer = new BinaryWriter(file)) {
                    int version = 102;
                    writer.Write(version);
                    writer.Write(m_wave_type);
                    writer.Write(m_sound_vol);
                    writer.Write(p_base_freq);
                    writer.Write(p_freq_limit);
                    writer.Write(p_freq_ramp);
                    writer.Write(p_freq_dramp);
                    writer.Write(p_duty);
                    writer.Write(p_duty_ramp);
                    writer.Write(p_vib_strength);
                    writer.Write(p_vib_speed);
                    writer.Write(p_vib_delay);
                    writer.Write(p_env_attack);
                    writer.Write(p_env_sustain);
                    writer.Write(p_env_decay);
                    writer.Write(p_env_punch);
                    writer.Write(m_filter_on);
                    writer.Write(p_lpf_resonance);
                    writer.Write(p_lpf_freq);
                    writer.Write(p_lpf_ramp);
                    writer.Write(p_hpf_freq);
                    writer.Write(p_hpf_ramp);
                    writer.Write(p_pha_offset);
                    writer.Write(p_pha_ramp);
                    writer.Write(p_repeat_speed);
                    writer.Write(p_arp_speed);
                    writer.Write(p_arp_mod);
                    return true;
                }
            }
        }
    }
}