#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec3 aNormal;

out vec2 vTexCoord;
out vec3 vWorldPos;
out vec3 vWorldNormal;
out vec3 vViewPos;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
uniform vec2 uUvScale;

void main() {
    vTexCoord = aTexCoord * uUvScale;
    vec4 worldPos = uModel * vec4(aPos, 1.0);
    vWorldPos = worldPos.xyz;
    vWorldNormal = mat3(transpose(inverse(uModel))) * aNormal;
    vViewPos = (uView * worldPos).xyz;
    gl_Position = uProj * uView * worldPos;
}