@binding(0) @group(0) var<storage, read> gaInput_0 : array<u32>;

@binding(1) @group(0) var<storage, read_write> gaOutput_0 : array<u32>;

@binding(2) @group(0) var<storage, read_write> gaFaults_0 : array<u32>;

@compute
@workgroup_size(64, 1, 1)
fn main(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S1 : u32 = id_0.x;
    if(_S1 >= gaInput_0[i32(0)])
    {
        return;
    }
    var gaRow_0 : u32 = _S1 * u32(2);
    var gaFault_0 : u32 = _S1 * u32(4);
    var gaInputWords_0 : u32 = (arrayLength(&(gaInput_0)));
    var _S2 : bool;
    if(gaInputWords_0 < u32(1))
    {
        _S2 = true;
    }
    else
    {
        _S2 = gaInput_0[i32(0)] > ((gaInputWords_0 - u32(1)) / u32(2));
    }
    if(_S2)
    {
        return;
    }
    var gaTickResourceStart_0 : u32 = u32(1) + gaInput_0[i32(0)] * u32(2);
    if((gaInputWords_0 - gaTickResourceStart_0) != u32(1))
    {
        return;
    }
    var f1_0 : u32 = gaInput_0[u32(1) + gaRow_0 + u32(1)];
    var t0_0 : u32;
    if((bitcast<i32>((f1_0))) <= (bitcast<i32>((gaInput_0[gaTickResourceStart_0]))))
    {
        t0_0 = u32(1);
    }
    else
    {
        t0_0 = u32(0);
    }
    gaOutput_0[gaRow_0] = t0_0;
    gaOutput_0[gaRow_0 + u32(1)] = f1_0;
    gaFaults_0[gaFault_0] = u32(0);
    gaFaults_0[gaFault_0 + u32(1)] = u32(0);
    gaFaults_0[gaFault_0 + u32(2)] = u32(0);
    gaFaults_0[gaFault_0 + u32(3)] = u32(0);
    return;
}
