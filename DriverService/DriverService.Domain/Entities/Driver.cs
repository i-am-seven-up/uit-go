using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriverService.Domain.Domain
{
    public class Driver
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FullName { get; set; } = string.Empty;
        public bool Online { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
