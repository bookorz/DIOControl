using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DIOControl.Config
{
    class ParamConfig
    {
        public string DeviceName { get; set; }
        public string Type { get; set; }
        public string Address { get; set; }
        public string Parameter { get; set; }
        public string Normal { get; set; }
        public string Abnormal { get; set; }
        public string Error_Code { get; set; }
        public DateTime LastErrorHappenTime { get; set; }
    }
}
