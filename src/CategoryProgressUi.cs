using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DiskRescue {
    public sealed partial class MainForm {
        void PaintCategoryProgress(object sender,DataGridViewCellPaintingEventArgs e) {
            if(e.RowIndex<0||e.ColumnIndex!=7)return;
            e.PaintBackground(e.ClipBounds,true);
            e.Paint(e.ClipBounds,DataGridViewPaintParts.Border);
            double ratio=e.Value is double?(double)e.Value:0;
            ratio=Math.Max(0,Math.Min(1,ratio));
            GraphicsState saved=e.Graphics.Save();
            try {
                e.Graphics.SetClip(Rectangle.Intersect(e.CellBounds,e.ClipBounds),CombineMode.Intersect);
                Rectangle track=new Rectangle(e.CellBounds.X+8,e.CellBounds.Bottom-9,Math.Max(1,e.CellBounds.Width-16),3);
                using(SolidBrush background=new SolidBrush(Color.FromArgb(232,237,245)))e.Graphics.FillRectangle(background,track);
                using(SolidBrush green=new SolidBrush(Color.FromArgb(17,150,113)))e.Graphics.FillRectangle(green,track.X,track.Y,(float)(track.Width*ratio),track.Height);
                TextRenderer.DrawText(e.Graphics,(ratio*100).ToString("0")+"%",categories.Font,new Rectangle(e.CellBounds.X+8,e.CellBounds.Y+2,Math.Max(1,e.CellBounds.Width-16),Math.Max(1,e.CellBounds.Height-11)),ink,TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter|TextFormatFlags.PreserveGraphicsClipping);
            }finally{e.Graphics.Restore(saved);}
            e.Handled=true;
        }
    }
}
