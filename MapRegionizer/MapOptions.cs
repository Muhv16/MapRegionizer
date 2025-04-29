using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapRegionizer
{
    public class MapOptions
    {
        public uint TargetSize { get; set; } = 200;
        private double _pointsMultiplier = 4;
        public double PointsMultiplier
        {
            get => _pointsMultiplier;
            set
            {
                if (value > 0)
                    _pointsMultiplier = value;
            }
        }

        private double _maxDownward = 0.75;
        public double MaxDownward
        {
            get => _maxDownward;
            set
            {
                if (value > 0)
                    _maxDownward = value;
            }
        }

        private double _maxUpward = 1.75;
        public double MaxUpward
        {
            get => _maxUpward;
            set
            {
                if (value > 0)
                    _maxUpward = value;
            }
        }

        private double _thresholdToSplit = 2;
        public double ThresholdToSplit
        {
            get => _thresholdToSplit;
            set
            {
                if (value > 0)
                    _thresholdToSplit = value;
            }
        }
    }
}
