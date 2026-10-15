using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DiskRescue {
    internal sealed class SoftButton:Button {
        public bool Primary;bool hover,pressed;
        public SoftButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;AutoSize=false;Font=new Font("Segoe UI",9);Cursor=Cursors.Hand;SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint,true);}
        protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Cursor=Enabled?Cursors.Hand:Cursors.Default;pressed=false;Invalidate();}
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=pressed=false;Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);pressed=e.Button==MouseButtons.Left;Invalidate();}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);pressed=false;Invalidate();}
        protected override void OnPaint(PaintEventArgs e){
            e.Graphics.Clear(RoundPanel.BackgroundOf(Parent));e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Color fill=!Enabled?Color.FromArgb(239,241,245):Primary?(pressed?Color.FromArgb(70,82,208):hover?Color.FromArgb(101,112,237):Color.FromArgb(89,101,233)):(pressed?Color.FromArgb(216,225,246):hover?Color.FromArgb(225,233,250):Color.FromArgb(235,240,252));
            Color text=!Enabled?Color.FromArgb(143,151,166):Primary?Color.White:Color.FromArgb(53,76,144);
            using(GraphicsPath shape=RoundPanel.Rounded(new Rectangle(0,0,Width-1,Height-1),8))using(SolidBrush brush=new SolidBrush(fill))e.Graphics.FillPath(brush,shape);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(Enabled&&Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(5,5,Width-10,Height-10),text,fill);
        }
    }
}
