using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UserService.Application.Abstractions
{
    public interface IJwtTokenProvider
    {
        string CreateToken(string userId, string email);
    }
}
