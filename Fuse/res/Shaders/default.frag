#version 330 core
in vec2 vTexCoord;
in vec3 vWorldPos;
in vec3 vWorldNormal;

out vec4 fragColor;

uniform sampler2D uTexture;
uniform bool uUseTexture;
uniform vec3 uColor;
uniform vec3 uLightDir;     // direção da luz (apontando PARA a fonte)
uniform vec3 uLightColor;
uniform float uAmbient;

void main() {
    vec3 N = normalize(vWorldNormal);
    vec3 L = normalize(uLightDir);
    float diff = max(dot(N, L), 0.0);
    float ambient = uAmbient;
    vec3 lighting = vec3(ambient) + (1.0 - ambient) * diff * uLightColor;

    vec4 texColor = texture(uTexture, vTexCoord);
    fragColor = texColor * vec4(uColor * lighting, 1.0);
}