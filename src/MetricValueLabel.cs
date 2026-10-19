using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Globalization;

namespace DiskRescue {
    public sealed class MetricNumberTransition {
        double from,target,started;
        public void Reset(double value){from=target=value;started=0;}
        public double ValueAt(double milliseconds){
            double t=Math.Max(0,Math.Min(1,(milliseconds-started)/200));
            if(t>=1)return target;
            double remaining=1-t;
            return from+(target-from)*(1-remaining*remaining*remaining);
        }
        public void Retarget(double value,double milliseconds){from=ValueAt(milliseconds);target=value;started=milliseconds;}
        public bool Finished(double milliseconds){return milliseconds-started>=200;}
    }
    // A metric is always one line; all metrics choose the same font size.
    public sealed class MetricValueLabel : Label {
        public Func<Graphics,float> FontSizeProvider;
        readonly MetricNumberTransition transition=new MetricNumberTransition();
        readonly Stopwatch clock=Stopwatch.StartNew();
        Timer animation;
        bool numeric;
        public MetricValueLabel(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}
        public string DisplayText {get{return numeric?Math.Round(transition.ValueAt(clock.Elapsed.TotalMilliseconds)).ToString("N0",CultureInfo.CurrentCulture):Text;}}
        public static readonly TextFormatFlags ValueFlags=TextFormatFlags.SingleLine|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter;
        protected override void OnTextChanged(EventArgs e){
            double target;
            if(transition!=null&&clock!=null){
                bool wasNumeric=numeric;
                numeric=Double.TryParse(Text,NumberStyles.Number,CultureInfo.CurrentCulture,out target)&&!Double.IsNaN(target)&&!Double.IsInfinity(target);
                if(animation!=null)animation.Stop();
                if(numeric){
                    if(!wasNumeric||!Visible||!IsHandleCreated||!SystemInformation.UIEffectsEnabled)transition.Reset(target);
                    else{
                        transition.Retarget(target,clock.Elapsed.TotalMilliseconds);
                        if(animation==null){animation=new Timer{Interval=16};animation.Tick+=delegate{if(transition.Finished(clock.Elapsed.TotalMilliseconds))animation.Stop();Invalidate();};}
                        animation.Start();
                    }
                }
            }
            base.OnTextChanged(e);
        }
        protected override void OnVisibleChanged(EventArgs e){
            if(animation!=null)animation.Stop();
            double target;
            if(transition!=null&&Double.TryParse(Text,NumberStyles.Number,CultureInfo.CurrentCulture,out target))transition.Reset(target);
            base.OnVisibleChanged(e);
        }
        protected override void Dispose(bool disposing){if(disposing&&animation!=null)animation.Dispose();base.Dispose(disposing);}
        protected override void OnPaint(PaintEventArgs e) {
            float size=FontSizeProvider==null?Font.Size:FontSizeProvider(e.Graphics);
            using(Font fitted=new Font(Font.FontFamily,size,Font.Style))
                TextRenderer.DrawText(e.Graphics,DisplayText,fitted,ClientRectangle,ForeColor,ValueFlags);
        }
    }
}
