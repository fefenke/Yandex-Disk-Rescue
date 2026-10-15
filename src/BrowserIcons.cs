using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DiskRescue {
    internal static class BrowserIcons {
        public static Image Make(string type,bool ready){
            Bitmap bitmap=new Bitmap(24,24);using(Graphics g=Graphics.FromImage(bitmap)){g.SmoothingMode=SmoothingMode.AntiAlias;
                if(type=="folder"){using(SolidBrush back=new SolidBrush(Color.FromArgb(224,157,43)))g.FillRectangle(back,2,5,9,15);using(SolidBrush gold=new SolidBrush(Color.FromArgb(249,193,70)))g.FillRectangle(gold,2,8,20,13);using(Pen edge=new Pen(Color.FromArgb(216,159,47)))g.DrawRectangle(edge,2,8,20,13);using(Pen shine=new Pen(Color.FromArgb(255,221,135)))g.DrawLine(shine,3,9,21,9);}
                else{using(SolidBrush paper=new SolidBrush(Color.FromArgb(242,246,253)))g.FillRectangle(paper,5,2,14,19);using(Pen edge=new Pen(Color.FromArgb(151,169,195)))g.DrawRectangle(edge,5,2,14,19);Color color=type=="video"?Color.FromArgb(151,106,209):type=="photo"?Color.FromArgb(56,151,199):Color.FromArgb(88,122,172);using(SolidBrush fill=new SolidBrush(color)){g.FillRectangle(fill,3,8,17,11);if(type=="video"){using(SolidBrush white=new SolidBrush(Color.White))g.FillPolygon(white,new[]{new Point(9,10),new Point(9,17),new Point(15,13)});}else if(type=="photo"){using(SolidBrush white=new SolidBrush(Color.White))g.FillPolygon(white,new[]{new Point(5,16),new Point(9,11),new Point(12,15),new Point(15,12),new Point(18,16)});}else using(Pen white=new Pen(Color.White,1.5f)){g.DrawLine(white,7,11,16,11);g.DrawLine(white,7,14,16,14);}}}
                if(ready){using(SolidBrush green=new SolidBrush(Color.FromArgb(21,151,107)))g.FillEllipse(green,13,13,11,11);using(Pen white=new Pen(Color.White,1.7f))g.DrawLines(white,new[]{new Point(16,18),new Point(18,20),new Point(21,16)});}
            }return bitmap;
        }
        public static string Kind(string path){string ext=Path.GetExtension(path).ToLowerInvariant();return new[]{".jpg",".jpeg",".png",".heic",".webp",".dng",".gif"}.ContainsExt(ext)?"photo":new[]{".mov",".mp4",".avi",".mkv"}.ContainsExt(ext)?"video":"document";}
        static bool ContainsExt(this string[] values,string value){return Array.IndexOf(values,value)>=0;}
    }
}
