using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Graphics.Canvas;

namespace Painting.Ink
{
    public class InkLayer : INotifyPropertyChanged
    {
        private string _name;
        private bool _isVisible;
        private bool _isLocked;

        public CanvasRenderTarget Image { get; internal set; }

        public string Name
        {
            get { return _name; }
            set { _name = value; OnPropertyChanged(); }
        }

        public bool IsVisible
        {
            get { return _isVisible; }
            set { _isVisible = value; OnPropertyChanged(); }
        }

        public bool IsLocked
        {
            get { return _isLocked; }
            set { _isLocked = value; OnPropertyChanged(); }
        }

        private void OnPropertyChanged([CallerMemberName]string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
