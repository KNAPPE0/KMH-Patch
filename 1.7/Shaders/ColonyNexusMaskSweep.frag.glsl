#version 450

// Colony Nexus light sweep: a leaning warm band that lights only the texture's shape (its alpha) as it crosses.

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUv;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;        // @color @default = vec4(1.0, 0.733, 0.420, 1.0)
    vec4 _MainTex_ST;   // @default = vec4(0.0, 0.0, 1.0, 1.0)
    float _SweepPos;    // @default = -1.0
    float _SweepAspect; // @default = 1.0
    float _SweepWidth;  // @default = 0.16
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

void main()
{
    vec2 uv = vUv * uMaterial._MainTex_ST.zw + uMaterial._MainTex_ST.xy;
    float shape = texture(_MainTex, uv).a;

    // GUI quads arrive with vUv.y = 1 at the top edge; p has its origin at the top-left, x in rect heights.
    vec2 p = vec2(vUv.x, 1.0 - vUv.y);
    float aspect = max(uMaterial._SweepAspect, 0.01);
    float width = max(uMaterial._SweepWidth, 0.01) * aspect;
    const float skew = 0.45;
    float x = p.x * aspect + (p.y - 0.5) * skew;

    // _SweepPos 0..1 carries the band across; anything outside means no light at all.
    float pos = uMaterial._SweepPos;
    float on = step(0.0, pos) * step(pos, 1.0);
    float head = mix(-0.5 * skew - 3.0 * width, aspect + 0.5 * skew + 3.0 * width, clamp(pos, 0.0, 1.0));
    float d = (x - head) / width;
    float light = (exp(-d * d) * 0.6 + exp(-d * d * 6.0) * 0.4) * on;

    outColor = vec4(uMaterial._Color.rgb * vColor.rgb, shape * light * uMaterial._Color.a * vColor.a);
}
