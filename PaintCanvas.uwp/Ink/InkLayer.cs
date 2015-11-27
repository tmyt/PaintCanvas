using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Graphics.Canvas;

namespace Painting.Ink
{
    public class InkLayer : INotifyPropertyChanged
    {
        private string _name;
        public CanvasRenderTarget Image { get; internal set; }

        public string Name
        {
            get { return _name; }
            set { _name = value; OnPropertyChanged(); }
        }

        private void OnPropertyChanged([CallerMemberName]string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
