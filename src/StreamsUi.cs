using System;
using System.Drawing;
using System.Windows.Forms;
namespace DiskRescue {
    public sealed partial class MainForm {
        void AddStreamsOptions(FlowLayoutPanel panel){
            streamsCaption=new Label{AutoSize=true,Margin=new Padding(18,5,4,0),ForeColor=ink};
            streamsPerFile=new NumericUpDown{Minimum=1,Maximum=8,Value=Math.Max(1,Math.Min(8,settings.StreamsPerFile)),Width=48,Margin=new Padding(0,1,8,0)};
            thresholdCaption=new Label{AutoSize=true,Margin=new Padding(4,5,4,0),ForeColor=ink};
            multiStreamThreshold=new NumericUpDown{Minimum=64,Maximum=4096,Increment=64,Value=Math.Max(64,Math.Min(4096,settings.MultiStreamThresholdMB)),Width=70,Margin=new Padding(0,1,0,0)};
            panel.Controls.Add(streamsCaption);panel.Controls.Add(streamsPerFile);panel.Controls.Add(thresholdCaption);panel.Controls.Add(multiStreamThreshold);
            streamsPerFile.ValueChanged+=delegate{if(!busy){settings.StreamsPerFile=(int)streamsPerFile.Value;SaveSettings();}};
            multiStreamThreshold.ValueChanged+=delegate{if(!busy){settings.MultiStreamThresholdMB=(int)multiStreamThreshold.Value;SaveSettings();}};
            TranslateStreams();
        }
        void TranslateStreams(){if(streamsPerFile==null)return;
            streamsCaption.Text=T("Потоков на крупный файл:","Streams per large file:");thresholdCaption.Text=T("Для файлов от, МБ:","For files from, MB:");
            tip.SetToolTip(streamsPerFile,T("1–8 потоков на один крупный файл, по умолчанию 4. Значение 1 отключает разделение новых файлов. Все потоки учитываются в общем лимите соединений скачивания. Ожидание очереди не расходует таймаут.","1–8 streams per large file; default 4. Set 1 to disable splitting new files. All streams count toward the shared download connection limit. Queue time does not consume the idle timeout."));
            tip.SetToolTip(multiStreamThreshold,T("Крупные файлы от этого размера делятся на части. 64–4096 МБ, по умолчанию 256. Данные частей сохраняются при обрывах и проверяются по MD5 после сборки.","Files at or above this size are split into ranges. 64–4096 MB; default 256. Parts survive interruptions and the assembled file is checked against MD5."));
        }
        void SetStreamsVisible(bool api){if(streamsPerFile==null)return;streamsCaption.Visible=streamsPerFile.Visible=thresholdCaption.Visible=multiStreamThreshold.Visible=api;streamsPerFile.Enabled=multiStreamThreshold.Enabled=api&&!busy;}
    }
}
