﻿using database_api.Models;
using database_api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using database_api.Data;

namespace database_api.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class RegistrationController : ControllerBase
    {
        private readonly UserManager<Users> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _webHostEnvironment;
        public RegistrationController(UserManager<Users> userManager,
            IConfiguration configuration, RoleManager<IdentityRole> roleManager, IWebHostEnvironment webHostEnvironment)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
        }


        [HttpPost("CreateRole")]
        public async Task<IActionResult> CreateRole(CreateRoleDTO roleDTO)
        {

            var response = await _roleManager.CreateAsync(new IdentityRole
            {
                Name = roleDTO.RoleName
            });

            if (response.Succeeded)
            {
                return Ok("New Role Created");
            }
            else
            {
                return BadRequest(response.Errors);
            }
        }


        [HttpPost("AssignRoleToUser")]
        public async Task<IActionResult> AssignRoleToUser(AssignRoleToUserDTO assignRoleToUserDTO)
        {

            var userDetails = await _userManager.FindByEmailAsync(assignRoleToUserDTO.Email);

            if (userDetails != null)
            {

                var userRoleAssignResponse = await _userManager.AddToRoleAsync(userDetails, assignRoleToUserDTO.RoleName);

                if (userRoleAssignResponse.Succeeded)
                {
                    return Ok("Role Assigned to User: " + assignRoleToUserDTO.RoleName);
                }
                else
                {
                    return BadRequest(userRoleAssignResponse.Errors);
                }
            }
            else
            {
                return BadRequest("There are no user exist with this email");
            }


        }

        // generates both tokens
        [AllowAnonymous]
        [HttpPost("RefreshToken")]
        public async Task<IActionResult> RefreshToken(RefreshTokenRequest refreshTokenRequest)
        {
            var response = new MainResponse();
            if (refreshTokenRequest is null)
            {
                response.ErrorMessage = "Invalid  request";
                return BadRequest(response);
            }

            var principal = GetPrincipalFromExpiredToken(refreshTokenRequest.AccessToken);

            if (principal != null)
            {
                var email = principal.Claims.FirstOrDefault(f => f.Type == ClaimTypes.Email);

                var user = await _userManager.FindByEmailAsync(email?.Value);

                if (user is null || user.RefreshToken != refreshTokenRequest.RefreshToken)
                {
                    response.ErrorMessage = "Invalid Request";
                    return BadRequest(response);
                }

                string newAccessToken = GenerateAccessToken(user);
                string refreshToken = GenerateRefreshToken();

                user.RefreshToken = refreshToken;
                await _userManager.UpdateAsync(user);

                response.IsSuccess = true;
                response.Content = new AuthenticationResponse
                {
                    RefreshToken = refreshToken,
                    AccessToken = newAccessToken
                };
                return Ok(response);
            }
            else
            {
                return ErrorResponse.ReturnErrorResponse("Invalid Token Found");
            }

        }


        //[AllowAnonymous]
        //[HttpPost("AuthenticateUser")]
        //public async Task<IActionResult> AuthenticateUser(AuthenticateUser authenticateUser)
        //{
        //    var user = await _userManager.FindByNameAsync(authenticateUser.UserName);
        //    if (user == null) return Unauthorized();

        //    bool isValidUser = await _userManager.CheckPasswordAsync(user, authenticateUser.Password);

        //    if (isValidUser)
        //    {
        //        var tokenHandler = new JwtSecurityTokenHandler();

        //        var keyDetail = Encoding.UTF8.GetBytes(_configuration["JWT:Key"]);

        //        var claims = new List<Claim>
        //        {
        //            new Claim(ClaimTypes.NameIdentifier, user.Id),
        //            new Claim(ClaimTypes.Name, $"{ user.FirstName} { user.LastName}"),
        //        };

        //        var tokenDescriptor = new SecurityTokenDescriptor
        //        {
        //            Audience = _configuration["JWT:Audience"],
        //            Issuer = _configuration["JWT:Issuer"],
        //            Expires = DateTime.UtcNow.AddDays(5),
        //            Subject = new ClaimsIdentity(claims),
        //            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyDetail), SecurityAlgorithms.HmacSha256Signature)
        //        };
        //        var token = tokenHandler.CreateToken(tokenDescriptor);
        //        return Ok(tokenHandler.WriteToken(token));
        //    }
        //    else
        //    {
        //        return Unauthorized();
        //    }
        //}

        // also generates both tokens
        [AllowAnonymous]
        [HttpPost("AuthenticateUser")]
        public async Task<IActionResult> AuthenticateUser(AuthenticateUser authenticateUser)
        {
            var user = await _userManager.FindByNameAsync(authenticateUser.UserName);
            if (user == null) return Unauthorized();

            bool isValidUser = await _userManager.CheckPasswordAsync(user, authenticateUser.Password);

            if (isValidUser)
            {
                string accessToken = GenerateAccessToken(user);
                var refreshToken = GenerateRefreshToken();
                user.RefreshToken = refreshToken;
                await _userManager.UpdateAsync(user);

                var response = new MainResponse
                {
                    Content = new AuthenticationResponse
                    {
                        RefreshToken = refreshToken,
                        AccessToken = accessToken
                    },
                    IsSuccess = true,
                    ErrorMessage = ""
                };
                return Ok(response);
            }
            else
            {
                return Unauthorized();
            }
        }

        private string GenerateAccessToken(Users user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            var keyDetail = Encoding.UTF8.GetBytes(_configuration["JWT:Key"]);

            var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Name, $"{user.FirstName} { user.LastName}"),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim("UserAvatar", $"{user.UserAvatar}"),
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Audience = _configuration["JWT:Audience"],
                Issuer = _configuration["JWT:Issuer"],
                Expires = DateTime.UtcNow.AddSeconds(5),
                Subject = new ClaimsIdentity(claims),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyDetail), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            var keyDetail = Encoding.UTF8.GetBytes(_configuration["JWT:Key"]);
            var tokenValidationParameter = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _configuration["JWT:Issuer"],
                ValidAudience = _configuration["JWT:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(keyDetail),
            };

            SecurityToken securityToken;
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameter, out securityToken);
            var jwtSecurityToken = securityToken as JwtSecurityToken;
            if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                throw new SecurityTokenException("Invalid token");
            return principal;
        }

        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
                return Convert.ToBase64String(randomNumber);
            }
        }

        [AllowAnonymous]
        [HttpPost("RegisterUser")]
        public async Task<IActionResult> RegisterUser(RegisterUserDTO registerUserDTO)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userToBeCreated = new Users
            {
                Email = registerUserDTO.Email,
                FirstName = registerUserDTO.FirstName,
                LastName = registerUserDTO.LastName,
                UserName = registerUserDTO.Email,
                Address = registerUserDTO.Address,
                Gender = registerUserDTO.Gender
            };


            if (!string.IsNullOrWhiteSpace(registerUserDTO.UserAvatar))
            {
                byte[] imgBytes = Convert.FromBase64String(registerUserDTO.UserAvatar);
                string fileName = $"{Guid.NewGuid()}_{userToBeCreated.FirstName.Trim()}_{userToBeCreated.LastName.Trim()}.jpeg";
                string avatar = await UploadFile(imgBytes, fileName);
                userToBeCreated.UserAvatar = avatar;
            }

            var response = await _userManager.CreateAsync(userToBeCreated, registerUserDTO.Password);
            if (response.Succeeded)
            {
                return Ok(new MainResponse
                {
                    IsSuccess = true,
                });
            }
            else
            {
                var errorDetails = string.Join(", ", response.Errors.Select(error => $"{error.Code}: {error.Description}"));
                return BadRequest(errorDetails);
            }
        }

        private async Task<string> UploadFile(byte[] bytes, string fileName)
        {
            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Images");
            string filePath = Path.Combine(uploadsFolder, fileName);

            // Ensure the directory exists
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            using (var fs = new FileStream(filePath, FileMode.Create))
            {
                await fs.WriteAsync(bytes, 0, bytes.Length);
            }

            return filePath;
        }

        /*[HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var existingUser = await _userManager.FindByIdAsync(id);

            if (existingUser != null)
            {
                var result = await _userManager.DeleteAsync(existingUser);

                if (result.Succeeded)
                {
                    return Ok(new MainResponse
                    {
                        IsSuccess = true,
                    });
                }
                else
                {
                    return ErrorResponse.ReturnErrorResponse(result.Errors?.ToString() ?? "");
                }
            }
            else
            {
                return ErrorResponse.ReturnErrorResponse("No User found with this ID");
            }
        }*/
    }
}