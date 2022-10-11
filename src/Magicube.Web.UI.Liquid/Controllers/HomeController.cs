using Magicube.Web.UI.Liquid.Entities;
using Magicube.Core;
using Magicube.Core.Models;
using Magicube.Core.Signals;
using Magicube.Data.Abstractions;
using Microsoft.AspNetCore.Mvc;
using System;
using System.ComponentModel.DataAnnotations;

namespace Magicube.Web.UI.Liquid.Controllers {
    public class HomeController : Controller {
        private readonly ISignal _signal;
        private readonly IRepository<WebPageEntity, int> _repository;

        public HomeController(IRepository<WebPageEntity, int> repository, ISignal signal) {
            _signal = signal;
            _repository = repository;
        }

        public IActionResult Index() {
            var entity = _repository.Get(x => x.Name == "about");
            return View(new LiquidPageViewModel { 
                Name = entity.Name,
                Path = entity.Path,
                Content = entity.Body,
            });
        }

        public IActionResult Render() {
            return View();
        }


        [HttpPost]
        public IActionResult AddOrUpdateLiquid([FromBody] LiquidPageViewModel model) {
            model.NotNull();

            var entity = _repository.Get(x=>x.Name == model.Name);
            if (entity == null) {
                _repository.Insert(new WebPageEntity { 
                    Name = model.Name,
                    Body = model.Content,
                    Path = model.Path,
                    Status = EntityStatus.Actived,
                    CreateAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                });
            } else {
                entity.Body = model.Content;
                _repository.Update(entity);
                _signal.SignalToken(model.Path);
            }
            return new JsonResult(true);
        }
    }

    public class LiquidPageViewModel {
        [Required]
        public string Name { get; set; }
        
        [Required]
        public string Content { get; set; }

        [Required]
        public string Path { get; set; }
    }
}
