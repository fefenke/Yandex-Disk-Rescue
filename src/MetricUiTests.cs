using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DiskRescue {
    static class MetricUiTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        public static void Run(string root){
            MetricNumberTransition transition=new MetricNumberTransition();transition.Reset(0);transition.Retarget(100,0);
            Assert(transition.ValueAt(0)==0,"Metric animation jumped at start.");
            double middle=transition.ValueAt(100);Assert(middle>0&&middle<100,"Metric animation has no intermediate value.");
            transition.Retarget(200,100);Assert(transition.ValueAt(100)==middle&&transition.ValueAt(300)==200,"Metric retarget is discontinuous.");
            transition.Retarget(10,300);Assert(transition.ValueAt(400)>10&&transition.ValueAt(400)<200&&transition.ValueAt(500)==10,"Decreasing metric animation failed.");
            using(MainForm form=new MainForm(Path.Combine(root,"metric-ui"))){
                BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
                MethodInfo render=typeof(MainForm).GetMethod("RenderCore",flags);
                DataGridView grid=(DataGridView)typeof(MainForm).GetField("categories",flags).GetValue(form);
                Snapshot snapshot=new Snapshot{Phase="running",Total=10000};
                CategoryStats category=new CategoryStats{Category="photos",Total=10000,Available=2000};snapshot.Categories.Add(category);
                render.Invoke(form,new object[]{snapshot});Assert((double)grid.Rows[0].Cells[7].Value==0.2,"Initial progress is incorrect.");
                int changed=0;grid.CellValueChanged+=delegate(object sender,DataGridViewCellEventArgs e){if(e.ColumnIndex==7)changed++;};
                category.Available=2001;render.Invoke(form,new object[]{snapshot});Assert(changed==1&&Math.Abs((double)grid.Rows[0].Cells[7].Value-0.2001)<0.0000001,"Sub-percent update did not invalidate progress.");
                category.Available=7700;render.Invoke(form,new object[]{snapshot});Assert(changed==2&&(double)grid.Rows[0].Cells[7].Value==0.77,"Progress requires a user click.");
                render.Invoke(form,new object[]{snapshot});Assert(changed==2,"Unchanged progress caused redundant updates.");
                Label[] labels=(Label[])typeof(MainForm).GetField("metricValue",flags).GetValue(form);
                MethodInfo fit=typeof(MainForm).GetMethod("MetricFontSize",flags);
                foreach(int dpi in new int[]{96,120,144,192})using(Bitmap image=new Bitmap(400,100)){
                    image.SetResolution(dpi,dpi);
                    using(Graphics graphics=Graphics.FromImage(image))foreach(int width in new int[]{95,110,180}){
                        string[] texts={"123 456 789","23 638","100 000","6 876","0"};
                        for(int i=0;i<labels.Length;i++){labels[i].Text=texts[i];labels[i].Size=new Size(width*dpi/96,46*dpi/96);}
                        float size=(float)fit.Invoke(form,new object[]{graphics});
                        using(Font font=new Font("Segoe UI",size,FontStyle.Bold))foreach(Label label in labels){
                            Size measured=TextRenderer.MeasureText(graphics,label.Text,font,new Size(Int32.MaxValue,Int32.MaxValue),MetricValueLabel.ValueFlags);
                            Assert(measured.Width<=label.Width&&measured.Height<=label.Height,"Large metric does not fit at scaled DPI.");
                        }
                    }
                }
            }
        }
    }
}
