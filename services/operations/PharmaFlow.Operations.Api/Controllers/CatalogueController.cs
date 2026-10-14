using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Operations.Api.Data;
using PharmaFlow.Operations.Api.Domain;
using PharmaFlow.Operations.Api.DTOs;

namespace PharmaFlow.Operations.Api.Controllers;

[ApiController]
[Route("api/catalogue")]
[Authorize]
public class CatalogueController : ControllerBase
{
    private readonly OperationsDbContext _db;

    public CatalogueController(OperationsDbContext db)
    {
        _db = db;
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.ParentCategoryId, c.IsActive))
            .ToListAsync();

        return Ok(ApiResponse<List<CategoryDto>>.Ok(categories));
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest req)
    {
        var category = new Category
        {
            Name = req.Name.Trim(),
            Description = req.Description,
            ParentCategoryId = req.ParentCategoryId,
            IsActive = true
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(category.Id, "Category created successfully."));
    }

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts(
        [FromQuery] int pageIndex = 0,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? categoryId = null)
    {
        var query = _db.Products.Include(p => p.Category).AsQueryable();

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p => p.BrandName.ToLower().Contains(s) ||
                                     p.GenericName.ToLower().Contains(s) ||
                                     p.Sku.ToLower().Contains(s) ||
                                     p.Barcodes.Any(b => b.Barcode.Contains(s)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.BrandName)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(p => new ProductDto(
                p.Id,
                p.Sku,
                p.BrandName,
                p.GenericName,
                p.Strength,
                p.DosageForm,
                p.CategoryId,
                p.Category.Name,
                p.StorageCondition,
                p.IsPrescriptionRequired,
                p.IsPublishedOnline,
                p.IsOrderEligible,
                p.BaseUnit,
                p.SellingPrice,
                p.CostPrice,
                p.TaxRatePercentage,
                p.ReorderThreshold,
                p.IsActive
            ))
            .ToListAsync();

        return Ok(ApiResponse<PagedResult<ProductDto>>.Ok(new PagedResult<ProductDto>(items, total, pageIndex, pageSize)));
    }

    [HttpGet("products/{id}")]
    public async Task<IActionResult> GetProductById(Guid id)
    {
        var p = await _db.Products
            .Include(x => x.Category)
            .Include(x => x.Barcodes)
            .Include(x => x.UnitConversions)
            .Include(x => x.StockBatches)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p == null) return NotFound(ApiResponse<ProductDetailDto>.Fail("Product not found."));

        var detail = new ProductDetailDto(
            p.Id,
            p.Sku,
            p.BrandName,
            p.GenericName,
            p.Strength,
            p.DosageForm,
            p.CategoryId,
            p.Category.Name,
            p.StorageCondition,
            p.IsPrescriptionRequired,
            p.IsPublishedOnline,
            p.IsOrderEligible,
            p.BaseUnit,
            p.SellingPrice,
            p.CostPrice,
            p.TaxRatePercentage,
            p.ReorderThreshold,
            p.IsActive,
            p.Barcodes.Select(b => b.Barcode).ToList(),
            p.UnitConversions.Select(uc => new UnitConversionDto(uc.Id, uc.PackUnitName, uc.MultiplierToBaseUnit, uc.PackSellingPrice)).ToList(),
            p.StockBatches.Where(sb => sb.Status == "Active").Sum(sb => sb.QuantityOnHand)
        );

        return Ok(ApiResponse<ProductDetailDto>.Ok(detail));
    }

    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest req)
    {
        if (!User.Claims.Any(c => c.Type == ClaimConstants.Permission && (c.Value == Permissions.CatalogueWrite || c.Value == Permissions.AdminBootstrap)))
        {
            return Forbid();
        }

        var existingSku = await _db.Products.FirstOrDefaultAsync(p => p.Sku.ToLower() == req.Sku.Trim().ToLower());
        if (existingSku != null)
        {
            return BadRequest(ApiResponse<Guid>.Fail("Product SKU already exists."));
        }

        if (req.UnitConversions != null && req.UnitConversions.Any(uc => uc.MultiplierToBaseUnit <= 0))
        {
            return BadRequest(ApiResponse<Guid>.Fail("Pack multiplier to base unit must be greater than zero."));
        }

        if (req.Barcodes != null)
        {
            foreach (var bc in req.Barcodes.Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                var exists = await _db.ProductBarcodes.AnyAsync(b => b.Barcode == bc.Trim());
                if (exists) return BadRequest(ApiResponse<Guid>.Fail($"Barcode '{bc}' is already assigned to another product."));
            }
        }

        var product = new Product
        {
            Sku = req.Sku.Trim().ToUpperInvariant(),
            BrandName = req.BrandName.Trim(),
            GenericName = req.GenericName.Trim(),
            Strength = req.Strength.Trim(),
            DosageForm = req.DosageForm.Trim(),
            CategoryId = req.CategoryId,
            StorageCondition = req.StorageCondition ?? "RoomTemperature",
            IsPrescriptionRequired = req.IsPrescriptionRequired,
            IsPublishedOnline = req.IsPublishedOnline,
            IsOrderEligible = req.IsOrderEligible,
            BaseUnit = req.BaseUnit ?? "Tablet",
            SellingPrice = req.SellingPrice,
            CostPrice = req.CostPrice,
            TaxRatePercentage = req.TaxRatePercentage,
            ReorderThreshold = req.ReorderThreshold,
            IsActive = true
        };

        if (req.Barcodes != null)
        {
            bool first = true;
            foreach (var bc in req.Barcodes.Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                product.Barcodes.Add(new ProductBarcode
                {
                    Barcode = bc.Trim(),
                    IsPrimary = first
                });
                first = false;
            }
        }

        if (req.UnitConversions != null)
        {
            foreach (var uc in req.UnitConversions)
            {
                product.UnitConversions.Add(new UnitConversion
                {
                    PackUnitName = uc.PackUnitName.Trim(),
                    MultiplierToBaseUnit = uc.MultiplierToBaseUnit,
                    PackSellingPrice = uc.PackSellingPrice
                });
            }
        }

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(product.Id, "Product created successfully."));
    }

    [HttpPut("products/{id}")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest req)
    {
        if (!User.Claims.Any(c => c.Type == ClaimConstants.Permission && (c.Value == Permissions.CatalogueWrite || c.Value == Permissions.AdminBootstrap)))
        {
            return Forbid();
        }

        var product = await _db.Products
            .Include(p => p.Barcodes)
            .Include(p => p.UnitConversions)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null) return NotFound(ApiResponse<bool>.Fail("Product not found."));

        if (req.UnitConversions != null && req.UnitConversions.Any(uc => uc.MultiplierToBaseUnit <= 0))
        {
            return BadRequest(ApiResponse<bool>.Fail("Pack multiplier to base unit must be greater than zero."));
        }

        if (req.Barcodes != null)
        {
            foreach (var bc in req.Barcodes.Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                var exists = await _db.ProductBarcodes.AnyAsync(b => b.Barcode == bc.Trim() && b.ProductId != id);
                if (exists) return BadRequest(ApiResponse<bool>.Fail($"Barcode '{bc}' is already assigned to another product."));
            }
        }

        product.BrandName = req.BrandName.Trim();
        product.GenericName = req.GenericName.Trim();
        product.Strength = req.Strength.Trim();
        product.DosageForm = req.DosageForm.Trim();
        product.CategoryId = req.CategoryId;
        if (!string.IsNullOrWhiteSpace(req.StorageCondition)) product.StorageCondition = req.StorageCondition;
        product.IsPrescriptionRequired = req.IsPrescriptionRequired;
        product.IsPublishedOnline = req.IsPublishedOnline;
        product.IsOrderEligible = req.IsOrderEligible;
        if (!string.IsNullOrWhiteSpace(req.BaseUnit)) product.BaseUnit = req.BaseUnit;
        product.SellingPrice = req.SellingPrice;
        product.CostPrice = req.CostPrice;
        product.TaxRatePercentage = req.TaxRatePercentage;
        product.ReorderThreshold = req.ReorderThreshold;
        product.IsActive = req.IsActive;

        if (req.Barcodes != null)
        {
            _db.ProductBarcodes.RemoveRange(product.Barcodes);
            bool first = true;
            foreach (var bc in req.Barcodes.Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                _db.ProductBarcodes.Add(new ProductBarcode
                {
                    ProductId = product.Id,
                    Barcode = bc.Trim(),
                    IsPrimary = first
                });
                first = false;
            }
        }

        if (req.UnitConversions != null)
        {
            _db.UnitConversions.RemoveRange(product.UnitConversions);
            foreach (var uc in req.UnitConversions)
            {
                _db.UnitConversions.Add(new UnitConversion
                {
                    ProductId = product.Id,
                    PackUnitName = uc.PackUnitName.Trim(),
                    MultiplierToBaseUnit = uc.MultiplierToBaseUnit,
                    PackSellingPrice = uc.PackSellingPrice
                });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Product updated successfully."));
    }

    [HttpGet("products/barcode/{barcode}")]
    public async Task<IActionResult> GetProductByBarcode(string barcode)
    {
        var bc = await _db.ProductBarcodes
            .Include(b => b.Product).ThenInclude(p => p.Category)
            .Include(b => b.Product).ThenInclude(p => p.StockBatches)
            .FirstOrDefaultAsync(b => b.Barcode == barcode.Trim());

        if (bc == null)
        {
            // Fallback check SKU as well
            var pBySku = await _db.Products
                .Include(p => p.Category)
                .Include(p => p.StockBatches)
                .FirstOrDefaultAsync(p => p.Sku == barcode.Trim());

            if (pBySku == null) return NotFound(ApiResponse<ProductDto>.Fail("No product found matching barcode."));

            return Ok(ApiResponse<ProductDto>.Ok(new ProductDto(
                pBySku.Id, pBySku.Sku, pBySku.BrandName, pBySku.GenericName, pBySku.Strength, pBySku.DosageForm,
                pBySku.CategoryId, pBySku.Category.Name, pBySku.StorageCondition, pBySku.IsPrescriptionRequired,
                pBySku.IsPublishedOnline, pBySku.IsOrderEligible, pBySku.BaseUnit, pBySku.SellingPrice,
                pBySku.CostPrice, pBySku.TaxRatePercentage, pBySku.ReorderThreshold, pBySku.IsActive
            )));
        }

        var p = bc.Product;
        return Ok(ApiResponse<ProductDto>.Ok(new ProductDto(
            p.Id, p.Sku, p.BrandName, p.GenericName, p.Strength, p.DosageForm,
            p.CategoryId, p.Category.Name, p.StorageCondition, p.IsPrescriptionRequired,
            p.IsPublishedOnline, p.IsOrderEligible, p.BaseUnit, p.SellingPrice,
            p.CostPrice, p.TaxRatePercentage, p.ReorderThreshold, p.IsActive
        )));
    }
}
