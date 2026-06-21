#version 330 core
in vec3 vWorldPos;

out vec4 fragColor;

uniform sampler2D uSkyTexture;

void main() {
    vec3 dir = normalize(vWorldPos);
    float u = 0.5 + atan(dir.z, dir.x) / (2.0 * 3.14159265);
    float v = 0.5 + asin(dir.y) / 3.14159265;
    fragColor = texture(uSkyTexture, vec2(u, v));
}