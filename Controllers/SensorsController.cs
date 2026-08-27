using AsrsWarehouse.Services;
using Microsoft.AspNetCore.Mvc;

namespace AsrsWarehouse.Controllers;

[ApiController]
[Route("api/sensors")]
public class SensorsController : ControllerBase
{
    private readonly WarehouseWorkflowService _workflow;
    public SensorsController(WarehouseWorkflowService workflow) => _workflow = workflow;

    public record SensorUpdate(string SlotName, bool Occupied);

    [HttpPost("update")]
    public async Task<IActionResult> Update([FromBody] SensorUpdate input, CancellationToken cancellationToken)
    {
        var result = await _workflow.UpdateSlotSensorAsync(input.SlotName, input.Occupied, cancellationToken);
        return result.Ok ? Ok(result) : NotFound(result);
    }
}
