using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DiskRescue {
    internal sealed class BufferedTree:TreeView {
        [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr mask,IntPtr value);
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);SendMessage(Handle,0x112C,new IntPtr(4),new IntPtr(4));}
        internal bool Buffered {get{return (SendMessage(Handle,0x112D,IntPtr.Zero,IntPtr.Zero).ToInt64()&4)!=0;}}
    }
    internal sealed class DarkMenuRenderer:ToolStripProfessionalRenderer {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){TextRenderer.DrawText(e.Graphics,e.Text,e.TextFont,e.TextRectangle,System.Drawing.Color.FromArgb(29,40,59),e.TextFormat);}
    }
    internal sealed class BufferedGrid:DataGridView {
        public BufferedGrid(){DoubleBuffered=true;}
    }
}
