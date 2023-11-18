using System;
using System.Windows.Forms;
using System.Collections.Generic;

namespace cs.sfxr
{
    static class Program
    {
        [STAThread]
        static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Sfxr());
        }
    }
}
