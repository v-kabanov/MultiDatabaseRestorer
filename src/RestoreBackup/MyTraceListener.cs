using System;
using System.Diagnostics;
using System.Windows.Controls;

namespace RestoreBackup
{
    public class MyTraceListener : TraceListener
    {
        private TextBox _output;

        public MyTraceListener(TextBox output)
        {
            this.Name = "Trace";
            this._output = output;
        }


        public override void Write(string message)
        {
            Action append = delegate()
            {
                _output.AppendText(message);
                _output.ScrollToEnd();
            };
            _output.Dispatcher.BeginInvoke(append);
        }

        public override void WriteLine(string message)
        {
            Write(message);
            Write(Environment.NewLine);
        }
    }
}
