#version 330 core
in vec3 vWorldPos;

uniform vec3 uLightPos;
uniform float uRadius;

void main()
{
    float dist = length(vWorldPos - uLightPos);
    gl_FragDepth = clamp(dist / uRadius, 0.0, 1.0);
}
