using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapRegionizer
{
    public class MapOptions
    {
        /// <summary>
        /// Целевая площадь региона
        /// </summary>
        public uint TargetSize { get; set; } = 200;
        private double _pointsMultiplier = 4;
        /// <summary>
        /// Множитель, влияющий на количество изначально генерируемых регионов
        /// </summary>
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
        /// <summary>
        /// Максимальное отклонение плозади региона от целевой в меньшую сторону
        /// </summary>
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
        /// <summary>
        /// Максимальное отклонение плозади региона от целевой в большую сторону
        /// </summary>
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
        /// <summary>
        /// Максимальное отклонение плозади региона от целевой, при которой тот будет разделен
        /// </summary>
        public double ThresholdToSplit
        {
            get => _thresholdToSplit;
            set
            {
                if (value > 0)
                    _thresholdToSplit = value;
            }
        }

        private double _distortionDetail = 0.4;
        /// <summary>
        /// Детализация (кол-во точек на пиксель) разбиения прямой на отдельные точки, по которым будет происходить искривление
        /// </summary>
        public double DistortionDetail
        {
            get => _distortionDetail;
            set
            {
                if (value > 0 && value <= 1) 
                    _distortionDetail = value;
            }
        }

        /// <summary>
        /// Максимальный отступ при искривлении прямой
        /// </summary>
        public double MaxOffst { get; set; } = 2;

        /// <summary>
        /// Минимальная длина, начиная от которой линия будет искривляться
        /// </summary>
        public double MinLineLenghtToCurve { get; set; } = 7;
    }
}
