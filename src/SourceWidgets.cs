using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DiskRescue {
    internal static class SourceArtwork {
        internal static void Draw(Graphics graphics,string name,Rectangle bounds,Color color){
            GraphicsState state=graphics.Save();graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.TranslateTransform(bounds.X,bounds.Y);graphics.ScaleTransform(bounds.Width/24f,bounds.Height/24f);
            using(var pen=new Pen(color,1.7f)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                if(name=="account"){graphics.DrawEllipse(pen,8,2,8,8);graphics.DrawBezier(pen,3,22,3,12,21,12,21,22);}
                else if(name=="link"){
                    graphics.TranslateTransform(12,12);graphics.RotateTransform(-40);
                    using(var left=RoundPanel.Rounded(new Rectangle(-11,-4,14,8),4))graphics.DrawPath(pen,left);
                    using(var right=RoundPanel.Rounded(new Rectangle(-3,-4,14,8),4))graphics.DrawPath(pen,right);
                    graphics.DrawLine(pen,-4,0,4,0);
                }else if(name=="cloud"){
                    using(var path=new GraphicsPath()){path.AddBezier(6,20,0,20,0,12,5,11);path.AddBezier(5,11,4,3,16,1,18,9);path.AddBezier(18,9,25,9,25,20,18,20);path.AddLine(18,20,6,20);graphics.DrawPath(pen,path);}
                }
            }graphics.Restore(state);
        }
    }
    internal sealed class SourceTabButton:RadioButton {
        internal string Glyph;bool hover;
        internal SourceTabButton(){Cursor=Cursors.Hand;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.FromArgb(239,242,250));var box=new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1));
            using(var shape=RoundPanel.Rounded(box,9)){
                if(Checked){using(var fill=new LinearGradientBrush(box,Color.FromArgb(48,85,247),Color.FromArgb(66,87,233),90f))g.FillPath(fill,shape);}
                else if(hover){using(var fill=new SolidBrush(Color.FromArgb(228,234,249)))g.FillPath(fill,shape);}
            }
            Color ink=Checked?Color.White:Color.FromArgb(28,36,57);int icon=Math.Min(23,Height-12),gap=10;int measured=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;
            int total=Math.Min(Width-20,measured+icon+gap),x=Math.Max(8,(Width-total)/2);
            SourceArtwork.Draw(g,Glyph,new Rectangle(x,(Height-icon)/2,icon,icon),ink);
            TextRenderer.DrawText(g,Text,Font,new Rectangle(x+icon+gap,0,Math.Max(1,Width-x-icon-gap-8),Height),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(5,5,Math.Max(1,Width-10),Math.Max(1,Height-10)),ink,Checked?Color.FromArgb(48,85,247):BackColor);
        }
    }
    internal sealed class SourceInfoButton:Button {
        bool hover;internal bool CloseIcon;
        internal SourceInfoButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.White);Color ink=hover?Color.FromArgb(48,85,247):Color.FromArgb(48,58,81);
            if(hover)using(var fill=new SolidBrush(Color.FromArgb(236,241,255)))g.FillEllipse(fill,1,1,Width-3,Height-3);
            using(var pen=new Pen(ink,1.5f)){pen.StartCap=pen.EndCap=LineCap.Round;if(CloseIcon){int inset=Width/3;g.DrawLine(pen,inset,inset,Width-inset,Height-inset);g.DrawLine(pen,Width-inset,inset,inset,Height-inset);}else g.DrawEllipse(pen,4,4,Width-9,Height-9);}
            if(!CloseIcon)TextRenderer.DrawText(g,"?",Font,ClientRectangle,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-2,-2));
        }
    }
    internal class SourceCard:Panel {
        internal SourceCard(){DoubleBuffered=true;BackColor=Color.White;}
        protected override void OnPaintBackground(PaintEventArgs e){
            e.Graphics.Clear(Color.White);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var box=new Rectangle(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3));using(var path=RoundPanel.Rounded(box,14))using(var fill=new SolidBrush(Color.White))using(var border=new Pen(Color.FromArgb(224,231,245))){e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(border,path);}
        }
    }
    internal sealed class SourceHelpPopup:ToolStripDropDown {
        internal SourceHelpPopup(){DoubleBuffered=true;Padding=Padding.Empty;AutoClose=true;DropShadowEnabled=true;BackColor=Color.White;}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width<32||Height<32)return;using(var path=RoundPanel.Rounded(new Rectangle(0,0,Width,Height),16)){var previous=Region;Region=new Region(path);if(previous!=null)previous.Dispose();}}
    }
    internal sealed class SourceHelpIcon:Control {
        internal string Glyph;
        internal SourceHelpIcon(){DoubleBuffered=true;Size=new Size(36,36);}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Color.White);using(var path=RoundPanel.Rounded(new Rectangle(0,0,Width-1,Height-1),9))using(var fill=new SolidBrush(Glyph=="account"?Color.FromArgb(229,235,255):Color.FromArgb(240,242,248)))e.Graphics.FillPath(fill,path);SourceArtwork.Draw(e.Graphics,Glyph,new Rectangle(7,7,Width-14,Height-14),Glyph=="account"?Color.FromArgb(48,85,247):Color.FromArgb(36,47,72));}
    }
    internal sealed class SourceInputFrame:Panel {
        readonly TextBox input;bool arranging;
        internal SourceInputFrame(TextBox field){
            input=field;DoubleBuffered=true;BackColor=Color.White;TabStop=false;input.BorderStyle=BorderStyle.None;input.Dock=DockStyle.None;input.Anchor=AnchorStyles.Left|AnchorStyles.Right;Controls.Add(input);
            input.GotFocus+=delegate{Invalidate();};input.LostFocus+=delegate{Invalidate();};input.FontChanged+=delegate{Arrange();};
        }
        void Arrange(){if(arranging||input==null)return;arranging=true;try{input.SetBounds(11,Math.Max(2,(ClientSize.Height-input.PreferredHeight)/2),Math.Max(1,ClientSize.Width-22),input.PreferredHeight);}finally{arranging=false;}}
        protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);Arrange();}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=RoundPanel.Rounded(new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1)),7))using(var pen=new Pen(input.Focused?Color.FromArgb(89,101,233):Color.FromArgb(191,200,219),input.Focused?1.5f:1f))e.Graphics.DrawPath(pen,path);}
    }
    internal sealed class SourcePublicWarning:Control {
        internal SourcePublicWarning(){DoubleBuffered=true;AccessibleRole=AccessibleRole.StaticText;Font=new Font("Segoe UI",9);BackColor=Color.White;}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.White);Color ink=Color.FromArgb(116,78,20);
            using(var path=RoundPanel.Rounded(new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1)),8))using(var fill=new SolidBrush(Color.FromArgb(255,248,232)))using(var border=new Pen(Color.FromArgb(240,218,171))){g.FillPath(fill,path);g.DrawPath(border,path);}
            int top=(Height-19)/2;using(var pen=new Pen(ink,1.4f))g.DrawEllipse(pen,11,top,19,19);TextRenderer.DrawText(g,"!",Font,new Rectangle(11,top,19,19),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g,Text,Font,new Rectangle(41,5,Math.Max(1,Width-52),Math.Max(1,Height-10)),ink,TextFormatFlags.WordBreak|TextFormatFlags.VerticalCenter|TextFormatFlags.TextBoxControl|TextFormatFlags.NoPadding);
        }
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);AccessibleName=Text;Invalidate();}
    }
}
