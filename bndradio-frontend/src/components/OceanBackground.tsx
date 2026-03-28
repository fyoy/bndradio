import { useEffect, useRef } from 'react'
import * as THREE from 'three'

// Вершинный шейдер — волны Герстнера + следы мыши
const WAVE_VERT = `
uniform float uTime;
uniform vec3  uRipples[12];
#define MAX_RIPPLES 12

varying vec2  vWorldXZ;
varying vec3  vNormal;
varying vec3  vViewDir;
varying float vFoam;
varying float vMouseRipple;
varying float vWaveHeight;

vec3 gerstner(vec2 pos, vec2 dir, float wavelength, float steepness, float speed, float t) {
  float k  = 6.2831853 / wavelength;
  float c  = sqrt(9.81 / k);
  float f  = k * (dot(dir, pos) - c * speed * t);
  float a  = steepness / k;
  return vec3(dir.x * a * cos(f), a * sin(f), dir.y * a * cos(f));
}

void main() {
  vec3 pos = position;
  vWorldXZ = pos.xz;

  vec3 w1 = gerstner(pos.xz, normalize(vec2( 1.0,  0.6)), 10.0, 0.28, 1.0, uTime);
  vec3 w2 = gerstner(pos.xz, normalize(vec2(-0.7,  1.0)),  7.0, 0.20, 1.2, uTime);
  vec3 w3 = gerstner(pos.xz, normalize(vec2( 0.3, -0.8)),  4.5, 0.14, 1.5, uTime);
  vec3 w4 = gerstner(pos.xz, normalize(vec2(-1.0,  0.3)), 15.0, 0.16, 0.7, uTime);
  pos += w1 + w2 + w3 + w4;

  float k1=6.2831853/10.0; float k2=6.2831853/7.0;
  float k3=6.2831853/4.5;  float k4=6.2831853/15.0;
  vec2 d1=normalize(vec2(1.0,0.6)); vec2 d2=normalize(vec2(-0.7,1.0));
  vec2 d3=normalize(vec2(0.3,-0.8)); vec2 d4=normalize(vec2(-1.0,0.3));
  float a1=0.28/k1; float a2=0.20/k2; float a3=0.14/k3; float a4=0.16/k4;
  float f1=k1*(dot(d1,position.xz)-sqrt(9.81/k1)*1.0*uTime);
  float f2=k2*(dot(d2,position.xz)-sqrt(9.81/k2)*1.2*uTime);
  float f3=k3*(dot(d3,position.xz)-sqrt(9.81/k3)*1.5*uTime);
  float f4=k4*(dot(d4,position.xz)-sqrt(9.81/k4)*0.7*uTime);

  vec3 tangent = vec3(1.0,0.0,0.0);
  tangent.y += d1.x*k1*a1*cos(f1)+d2.x*k2*a2*cos(f2)+d3.x*k3*a3*cos(f3)+d4.x*k4*a4*cos(f4);
  vec3 binorm = vec3(0.0,0.0,1.0);
  binorm.y  += d1.y*k1*a1*cos(f1)+d2.y*k2*a2*cos(f2)+d3.y*k3*a3*cos(f3)+d4.y*k4*a4*cos(f4);
  vNormal = normalize(cross(binorm, tangent));

  float rippleDisp = 0.0;
  float rippleAcc  = 0.0;
  for (int i = 0; i < MAX_RIPPLES; i++) {
    vec2  rPos  = uRipples[i].xy;
    float rBorn = uRipples[i].z;
    if (rBorn < 0.0) continue;
    float age    = uTime - rBorn;
    float radius = age * 3.5;
    float dist   = length(pos.xz - rPos);
    float decay  = exp(-age * 1.2);
    float ring   = exp(-pow(dist - radius, 2.0) * 1.8) * decay;
    float wave   = sin(dist * 2.5 - age * 6.0) * ring * 0.18;
    rippleDisp  += wave;
    rippleAcc   += ring * decay;
  }
  pos.y += rippleDisp;
  vMouseRipple = clamp(rippleAcc, 0.0, 1.0);
  vWaveHeight  = clamp((pos.y + 0.5) / 1.2, 0.0, 1.0);
  vFoam = clamp((pos.y + 0.15) / 0.35, 0.0, 1.0);

  vec4 worldPos = modelMatrix * vec4(pos, 1.0);
  vViewDir = normalize(cameraPosition - worldPos.xyz);
  gl_Position = projectionMatrix * viewMatrix * worldPos;
}
`

// Фрагментный шейдер — Fresnel, отражение неба, дорожка светила, пена, туман
const WAVE_FRAG = `
uniform vec3  uSunDir;
uniform vec3  uSunColor;
uniform vec3  uSkyZenith;
uniform vec3  uSkyHorizon;
uniform float uTime;
uniform float uNightBlend;
uniform vec2  uCelestialNDC; // экранные координаты светила [-1..1]

varying vec2  vWorldXZ;
varying vec3  vNormal;
varying vec3  vViewDir;
varying float vFoam;
varying float vMouseRipple;
varying float vWaveHeight;

float hash(vec2 p){ return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
float noise(vec2 p){
  vec2 i=floor(p); vec2 f=fract(p);
  f=f*f*(3.0-2.0*f);
  return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),
             mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),f.x),f.y);
}

void main() {
  vec2 rippleUV = vWorldXZ * 0.8;
  float r1 = noise(rippleUV + vec2(uTime*0.4, uTime*0.3)) * 2.0 - 1.0;
  float r2 = noise(rippleUV * 2.3 - vec2(uTime*0.5, uTime*0.2)) * 2.0 - 1.0;
  vec3 rippleN = normalize(vNormal + vec3(r1*0.12, 0.0, r2*0.12));

  float cosTheta = max(dot(rippleN, vViewDir), 0.0);
  float fresnel = mix(0.04, 1.0, pow(1.0 - cosTheta, 4.0));

  vec3 deepColor    = mix(vec3(0.0, 0.04, 0.16), vec3(0.0, 0.01, 0.07), uNightBlend);
  vec3 shallowColor = mix(vec3(0.02, 0.28, 0.52), vec3(0.01, 0.10, 0.28), uNightBlend);
  float depthVis = pow(cosTheta, 1.5);
  vec3 waterBody = mix(deepColor, shallowColor, depthVis * 0.7 + vFoam * 0.3);

  vec3 subsurface = mix(vec3(0.0, 0.18, 0.35), vec3(0.0, 0.06, 0.18), uNightBlend);
  waterBody = mix(waterBody, subsurface, (1.0 - fresnel) * 0.35);

  vec3 reflDir = reflect(-vViewDir, rippleN);
  float skyT = clamp(reflDir.y * 0.5 + 0.5, 0.0, 1.0);
  vec3 skyRefl = mix(uSkyHorizon, uSkyZenith, pow(skyT, 0.6));

  // --- дорожка светила на воде ---
  // отражённый вектор взгляда в NDC-пространстве (приближение)
  vec3 reflDirN = normalize(reflDir);
  // угол между отражением и направлением на светило
  float sunDot = dot(reflDirN, normalize(uSunDir));
  // широкая мягкая дорожка
  float trail = pow(max(sunDot, 0.0), 6.0) * 1.8;
  // узкий яркий блик поверх
  float trailSharp = pow(max(sunDot, 0.0), 80.0) * 3.0;
  // модулируем рябью чтобы дорожка "мерцала"
  vec2 microUV2 = vWorldXZ * 2.2;
  float rippleMod = 0.6 + 0.4 * noise(microUV2 + vec2(uTime*0.6, uTime*0.4));
  trail *= rippleMod;
  trailSharp *= rippleMod;
  vec3 trailColor = uSunColor * (trail + trailSharp);

  // Blinn-Phong блик
  vec3 halfV = normalize(uSunDir + vViewDir);
  float spec = pow(max(dot(rippleN, halfV), 0.0), 256.0);
  float specNight = pow(max(dot(rippleN, halfV), 0.0), 512.0);
  vec3 specColor = uSunColor * mix(spec * 1.8, specNight * 0.6, uNightBlend);

  vec2 microUV = vWorldXZ * 3.5;
  float m1 = noise(microUV + vec2(uTime*0.9, uTime*0.6));
  float m2 = noise(microUV * 1.8 - vec2(uTime*0.7, uTime*1.1));
  vec3 microN = normalize(vec3((m1-0.5)*0.25, 1.0, (m2-0.5)*0.25));
  float microSpec = pow(max(dot(microN, normalize(uSunDir + vViewDir)), 0.0), 512.0);
  specColor += uSunColor * microSpec * mix(0.9, 0.3, uNightBlend);

  vec3 color = mix(waterBody, skyRefl, fresnel * 0.6);
  color += specColor;
  color += trailColor * mix(0.55, 0.35, uNightBlend);

  // пена на гребнях волн
  float foam = smoothstep(0.6, 0.85, vFoam);
  color = mix(color, vec3(0.92, 0.96, 1.0), foam * 0.5);

  // след мыши
  color = mix(color, color * 1.3 + vec3(0.05, 0.12, 0.18), vMouseRipple * 0.5);

  // туман к горизонту — плавное смешение с цветом горизонта
  float depth = gl_FragCoord.z / gl_FragCoord.w;
  float fogFactor = exp(-depth * 0.028);
  color = mix(uSkyHorizon * 0.85, color, clamp(fogFactor, 0.0, 1.0));

  gl_FragColor = vec4(color, 1.0);
}
`

const SKY_VERT = `
varying vec2 vUv;
void main() {
  vUv = uv;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}
`
const SKY_FRAG = `
uniform vec3 uHorizon;
uniform vec3 uZenith;
varying vec2 vUv;
void main() {
  vec3 col = mix(uHorizon, uZenith, pow(vUv.y, 0.6));
  gl_FragColor = vec4(col, 1.0);
}
`

const STAR_VERT = `
uniform float uTime;
attribute float aSize;
attribute float aPhase;
varying float vAlpha;
void main() {
  vAlpha = 0.5 + 0.5 * sin(uTime * 1.8 + aPhase);
  vec4 mv = modelViewMatrix * vec4(position, 1.0);
  gl_PointSize = aSize * (1.0 + 0.3 * sin(uTime * 2.5 + aPhase));
  gl_Position = projectionMatrix * mv;
}
`
const STAR_FRAG = `
uniform float uNightAlpha;
varying float vAlpha;
void main() {
  float d = length(gl_PointCoord - vec2(0.5));
  if (d > 0.5) discard;
  float a = smoothstep(0.5, 0.1, d) * vAlpha * uNightAlpha;
  gl_FragColor = vec4(1.0, 0.97, 0.9, a);
}
`

const CLOUD_VERT = `
varying vec2 vUv;
void main() {
  vUv = uv;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}
`
const CLOUD_FRAG = `
uniform float uTime;
uniform float uOffset;
uniform float uAlpha;
varying vec2 vUv;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
  vec2 i = floor(p); vec2 f = fract(p);
  f = f * f * (3.0 - 2.0 * f);
  return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),
             mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),f.x),f.y);
}
float fbm(vec2 p) {
  float v = 0.0;
  v += noise(p) * 0.5;
  v += noise(p * 2.1 + vec2(1.7, 9.2)) * 0.25;
  v += noise(p * 4.3 + vec2(8.3, 2.8)) * 0.125;
  return v;
}
void main() {
  vec2 uv = vUv;
  uv.x += uTime * 0.012 + uOffset;
  float cloud = fbm(uv * 3.0);
  cloud = smoothstep(0.42, 0.72, cloud);
  float fadeX = smoothstep(0.0, 0.18, vUv.x) * smoothstep(1.0, 0.82, vUv.x);
  float fadeY = smoothstep(0.0, 0.25, vUv.y) * smoothstep(1.0, 0.55, vUv.y);
  gl_FragColor = vec4(1.0, 1.0, 1.0, cloud * fadeX * fadeY * uAlpha);
}
`

// Шейдер частиц пены
const FOAM_VERT = `
uniform float uTime;
attribute float aOffset;
attribute float aSpeed;
attribute float aWavePhase;
varying float vAlpha;
void main() {
  vec3 pos = position;
  // дрейф по поверхности
  pos.x += sin(uTime * aSpeed * 0.4 + aOffset) * 0.6;
  pos.z += cos(uTime * aSpeed * 0.3 + aOffset * 1.3) * 0.4;
  // вертикальное покачивание на волне
  pos.y += sin(uTime * aSpeed + aWavePhase) * 0.18;
  vAlpha = 0.3 + 0.4 * abs(sin(uTime * 0.7 + aOffset));
  vec4 mv = modelViewMatrix * vec4(pos, 1.0);
  gl_PointSize = 3.0 * (1.0 / -mv.z) * 120.0;
  gl_Position = projectionMatrix * mv;
}
`
const FOAM_FRAG = `
varying float vAlpha;
void main() {
  float d = length(gl_PointCoord - vec2(0.5));
  if (d > 0.5) discard;
  float a = smoothstep(0.5, 0.15, d) * vAlpha;
  gl_FragColor = vec4(0.9, 0.96, 1.0, a);
}
`

function getSkyColors(hour: number) {
  if (hour >= 6 && hour < 12) {
    const p = (hour - 6) / 6
    return {
      horizon: lerpColor([1.0, 0.6, 0.3], [0.53, 0.81, 0.98], p),
      zenith:  lerpColor([0.2, 0.1, 0.4], [0.1, 0.4, 0.9], p),
      isDay: true, nightAlpha: 0,
    }
  } else if (hour >= 12 && hour < 18) {
    const p = (hour - 12) / 6
    return {
      horizon: lerpColor([0.53, 0.81, 0.98], [1.0, 0.4, 0.1], p),
      zenith:  lerpColor([0.1, 0.4, 0.9], [0.15, 0.05, 0.3], p),
      isDay: true, nightAlpha: 0,
    }
  } else if (hour >= 18 && hour < 21) {
    const p = (hour - 18) / 3
    return {
      horizon: lerpColor([1.0, 0.4, 0.1], [0.05, 0.05, 0.2], p),
      zenith:  lerpColor([0.15, 0.05, 0.3], [0.02, 0.02, 0.12], p),
      isDay: p < 0.5, nightAlpha: p,
    }
  } else if (hour >= 21 || hour < 5) {
    return {
      horizon: [0.02, 0.03, 0.12] as [number,number,number],
      zenith:  [0.01, 0.01, 0.06] as [number,number,number],
      isDay: false, nightAlpha: 1,
    }
  } else {
    const p = (hour - 5) / 1
    return {
      horizon: lerpColor([0.05, 0.05, 0.2], [1.0, 0.6, 0.3], p),
      zenith:  lerpColor([0.02, 0.02, 0.12], [0.2, 0.1, 0.4], p),
      isDay: p > 0.5, nightAlpha: 1 - p,
    }
  }
}

function lerpColor(a: [number,number,number], b: [number,number,number], t: number): [number,number,number] {
  return [a[0]+(b[0]-a[0])*t, a[1]+(b[1]-a[1])*t, a[2]+(b[2]-a[2])*t]
}

export default function OceanBackground({ timeOverride }: { timeOverride?: 'day' | 'night' }) {
  const mountRef = useRef<HTMLDivElement>(null)
  const timeOverrideRef = useRef(timeOverride)
  useEffect(() => { timeOverrideRef.current = timeOverride }, [timeOverride])

  useEffect(() => {
    const mount = mountRef.current
    if (!mount) return

    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false })
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
    renderer.setSize(window.innerWidth, window.innerHeight)
    mount.appendChild(renderer.domElement)

    const scene = new THREE.Scene()
    const camera = new THREE.PerspectiveCamera(55, window.innerWidth / window.innerHeight, 0.1, 1000)
    camera.position.set(0, 5, 16)
    camera.lookAt(0, 0, 0)

    // целевая позиция камеры для параллакса
    const camTarget = new THREE.Vector3(0, 5, 16)
    const camLookTarget = new THREE.Vector3(0, 0, 0)
    let mouseParallaxX = 0, mouseParallaxY = 0

    // --- небо ---
    const skyGeo = new THREE.PlaneGeometry(200, 80)
    const skyMat = new THREE.ShaderMaterial({
      vertexShader: SKY_VERT, fragmentShader: SKY_FRAG,
      uniforms: { uHorizon: { value: new THREE.Color() }, uZenith: { value: new THREE.Color() } },
      depthWrite: false,
    })
    const skyMesh = new THREE.Mesh(skyGeo, skyMat)
    skyMesh.position.set(0, 10, -40)
    skyMesh.rotation.x = -0.1
    skyMesh.renderOrder = 0
    scene.add(skyMesh)

    // --- звёзды ---
    const STAR_COUNT = 300
    const starPos = new Float32Array(STAR_COUNT * 3)
    const starSizes = new Float32Array(STAR_COUNT)
    const starPhases = new Float32Array(STAR_COUNT)
    for (let i = 0; i < STAR_COUNT; i++) {
      starPos[i*3]   = (Math.random()-0.5)*160
      starPos[i*3+1] = 8 + Math.random()*28
      starPos[i*3+2] = -38 + Math.random()*4
      starSizes[i]   = 1.5 + Math.random()*2.5
      starPhases[i]  = Math.random()*Math.PI*2
    }
    const starGeo = new THREE.BufferGeometry()
    starGeo.setAttribute('position', new THREE.BufferAttribute(starPos, 3))
    starGeo.setAttribute('aSize',    new THREE.BufferAttribute(starSizes, 1))
    starGeo.setAttribute('aPhase',   new THREE.BufferAttribute(starPhases, 1))
    const starMat = new THREE.ShaderMaterial({
      vertexShader: STAR_VERT, fragmentShader: STAR_FRAG,
      uniforms: { uTime: { value: 0 }, uNightAlpha: { value: 0 } },
      transparent: true, depthWrite: false,
    })
    const starPoints = new THREE.Points(starGeo, starMat)
    starPoints.renderOrder = 1
    scene.add(starPoints)

    // --- облака ---
    const cloudMats: THREE.ShaderMaterial[] = []
    for (let i = 0; i < 3; i++) {
      const cGeo = new THREE.PlaneGeometry(160, 30)
      const cMat = new THREE.ShaderMaterial({
        vertexShader: CLOUD_VERT, fragmentShader: CLOUD_FRAG,
        uniforms: { uTime: { value: 0 }, uOffset: { value: i*1.3 }, uAlpha: { value: 0 } },
        transparent: true, depthWrite: false,
      })
      const cMesh = new THREE.Mesh(cGeo, cMat)
      cMesh.position.set(0, 14+i*4, -36+i*2)
      cMesh.rotation.x = -0.08
      cMesh.renderOrder = 1
      scene.add(cMesh)
      cloudMats.push(cMat)
    }

    // --- солнце / луна ---
    const celestialGeo = new THREE.CircleGeometry(1.8, 32)
    const celestialMat = new THREE.MeshBasicMaterial({ color: 0xfffbe0, depthWrite: false, depthTest: false })
    const celestialMesh = new THREE.Mesh(celestialGeo, celestialMat)
    celestialMesh.renderOrder = 1
    scene.add(celestialMesh)
    const glowGeo = new THREE.CircleGeometry(2.8, 32)
    const glowMat = new THREE.MeshBasicMaterial({ color: 0xfffbe0, transparent: true, opacity: 0.18, depthWrite: false, depthTest: false })
    const glowMesh = new THREE.Mesh(glowGeo, glowMat)
    glowMesh.renderOrder = 1
    scene.add(glowMesh)

    // --- море ---
    const SEG = 160
    const oceanGeo = new THREE.PlaneGeometry(80, 60, SEG, SEG)
    oceanGeo.rotateX(-Math.PI / 2)
    const oceanMat = new THREE.ShaderMaterial({
      vertexShader: WAVE_VERT,
      fragmentShader: WAVE_FRAG,
      uniforms: {
        uTime:          { value: 0 },
        uSunDir:        { value: new THREE.Vector3(0, 1, 0.5).normalize() },
        uSunColor:      { value: new THREE.Color(1.0, 0.95, 0.8) },
        uSkyZenith:     { value: new THREE.Color(0.1, 0.4, 0.9) },
        uSkyHorizon:    { value: new THREE.Color(0.53, 0.81, 0.98) },
        uNightBlend:    { value: 0 },
        uRipples:       { value: Array.from({ length: 12 }, () => new THREE.Vector3(0, 0, -1)) },
        uCelestialNDC:  { value: new THREE.Vector2(0, 0.5) },
      },
      transparent: false,
    })
    const ocean = new THREE.Mesh(oceanGeo, oceanMat)
    ocean.position.y = -1
    ocean.renderOrder = 2
    scene.add(ocean)

    // --- частицы пены ---
    const FOAM_COUNT = 120
    const foamPos    = new Float32Array(FOAM_COUNT * 3)
    const foamOffset = new Float32Array(FOAM_COUNT)
    const foamSpeed  = new Float32Array(FOAM_COUNT)
    const foamPhase  = new Float32Array(FOAM_COUNT)
    for (let i = 0; i < FOAM_COUNT; i++) {
      foamPos[i*3]   = (Math.random()-0.5) * 60
      foamPos[i*3+1] = -0.5 + Math.random() * 0.4
      foamPos[i*3+2] = (Math.random()-0.5) * 40
      foamOffset[i]  = Math.random() * Math.PI * 2
      foamSpeed[i]   = 0.5 + Math.random() * 1.0
      foamPhase[i]   = Math.random() * Math.PI * 2
    }
    const foamGeo = new THREE.BufferGeometry()
    foamGeo.setAttribute('position', new THREE.BufferAttribute(foamPos, 3))
    foamGeo.setAttribute('aOffset',  new THREE.BufferAttribute(foamOffset, 1))
    foamGeo.setAttribute('aSpeed',   new THREE.BufferAttribute(foamSpeed, 1))
    foamGeo.setAttribute('aWavePhase', new THREE.BufferAttribute(foamPhase, 1))
    const foamMat = new THREE.ShaderMaterial({
      vertexShader: FOAM_VERT, fragmentShader: FOAM_FRAG,
      uniforms: { uTime: { value: 0 } },
      transparent: true, depthWrite: false,
    })
    const foamPoints = new THREE.Points(foamGeo, foamMat)
    foamPoints.renderOrder = 3
    scene.add(foamPoints)

    let rafId = 0
    const clock = new THREE.Clock()
    const ORBIT_R = 22, ORBIT_CX = 0, ORBIT_CY = 14, ORBIT_Z = -35

    // --- следы мыши ---
    const MAX_RIPPLES = 12
    const ripples = oceanMat.uniforms.uRipples.value as THREE.Vector3[]
    let rippleIdx = 0
    let lastMouseX = -1, lastMouseY = -1
    const mousePlane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 1)
    const raycaster = new THREE.Raycaster()
    const mouseNDC = new THREE.Vector2()
    const hitPoint = new THREE.Vector3()

    const onMouseMove = (e: MouseEvent) => {
      const nx = (e.clientX / window.innerWidth) * 2 - 1
      const ny = -(e.clientY / window.innerHeight) * 2 + 1

      // параллакс камеры
      mouseParallaxX = nx * 1.4
      mouseParallaxY = ny * 0.7

      const dx = e.clientX - lastMouseX
      const dy = e.clientY - lastMouseY
      const speed = Math.sqrt(dx*dx + dy*dy)
      lastMouseX = e.clientX
      lastMouseY = e.clientY
      if (speed < 4) return

      mouseNDC.set(nx, ny)
      raycaster.setFromCamera(mouseNDC, camera)
      if (raycaster.ray.intersectPlane(mousePlane, hitPoint)) {
        ripples[rippleIdx % MAX_RIPPLES].set(hitPoint.x, hitPoint.z, clock.getElapsedTime())
        rippleIdx++
      }
    }
    window.addEventListener('mousemove', onMouseMove)

    const animate = () => {
      const elapsed = clock.getElapsedTime()
      const now = new Date()
      const hour = timeOverrideRef.current === 'day' ? 12
                 : timeOverrideRef.current === 'night' ? 0
                 : now.getHours() + now.getMinutes() / 60
      const sky = getSkyColors(hour)

      skyMat.uniforms.uHorizon.value.setRGB(...sky.horizon)
      skyMat.uniforms.uZenith.value.setRGB(...sky.zenith)

      const dayFraction = (hour - 6) / 12
      const sunAngle = (dayFraction - 0.5) * Math.PI
      const cx = ORBIT_CX + Math.sin(sunAngle) * ORBIT_R
      const cy = ORBIT_CY + Math.cos(sunAngle) * ORBIT_R * 0.55
      celestialMesh.position.set(cx, cy, ORBIT_Z + 2)
      glowMesh.position.set(cx, cy, ORBIT_Z + 1)

      const aboveHorizon = cy > ORBIT_CY - 2
      celestialMesh.visible = aboveHorizon
      glowMesh.visible = aboveHorizon

      if (sky.isDay) {
        const sunT = Math.abs(Math.cos(sunAngle))
        const sunColor = new THREE.Color().lerpColors(new THREE.Color(0xffcc66), new THREE.Color(0xfffbe0), sunT)
        celestialMat.color.copy(sunColor)
        glowMat.color.copy(sunColor)
        glowMat.opacity = 0.18 + sunT * 0.1
        oceanMat.uniforms.uSunColor.value.setRGB(sunColor.r * 1.2, sunColor.g * 1.1, sunColor.b * 0.9)
      } else {
        celestialMat.color.set(0xdde8ff)
        glowMat.color.set(0xaabbdd)
        glowMat.opacity = 0.12
        oceanMat.uniforms.uSunColor.value.setRGB(0.7, 0.75, 0.9)
      }

      // NDC координаты светила для шейдера дорожки
      const celestialWorld = new THREE.Vector3(cx, cy, ORBIT_Z + 2)
      const celestialNDC = celestialWorld.clone().project(camera)
      oceanMat.uniforms.uCelestialNDC.value.set(celestialNDC.x, celestialNDC.y)

      starMat.uniforms.uTime.value = elapsed
      starMat.uniforms.uNightAlpha.value = sky.nightAlpha

      const cloudAlpha = sky.isDay ? 0.22 : sky.nightAlpha * 0.1
      cloudMats.forEach((m, i) => {
        m.uniforms.uTime.value = elapsed
        m.uniforms.uAlpha.value = cloudAlpha + i * 0.04
      })

      const sunDir = new THREE.Vector3(cx, cy, ORBIT_Z).normalize()
      oceanMat.uniforms.uSunDir.value.copy(sunDir)
      oceanMat.uniforms.uTime.value = elapsed
      oceanMat.uniforms.uNightBlend.value = sky.nightAlpha
      oceanMat.uniforms.uSkyZenith.value.setRGB(...sky.zenith)
      oceanMat.uniforms.uSkyHorizon.value.setRGB(...sky.horizon)

      foamMat.uniforms.uTime.value = elapsed

      // плавный параллакс камеры (lerp к цели)
      camTarget.set(mouseParallaxX, 5 + mouseParallaxY * 0.5, 16)
      camera.position.lerp(camTarget, 0.04)
      camLookTarget.set(mouseParallaxX * 0.3, mouseParallaxY * 0.2, 0)
      camera.lookAt(camLookTarget)

      renderer.render(scene, camera)
      rafId = requestAnimationFrame(animate)
    }

    rafId = requestAnimationFrame(animate)

    const onResize = () => {
      camera.aspect = window.innerWidth / window.innerHeight
      camera.updateProjectionMatrix()
      renderer.setSize(window.innerWidth, window.innerHeight)
    }
    window.addEventListener('resize', onResize)

    return () => {
      cancelAnimationFrame(rafId)
      window.removeEventListener('resize', onResize)
      window.removeEventListener('mousemove', onMouseMove)
      renderer.dispose()
      oceanGeo.dispose(); oceanMat.dispose()
      skyGeo.dispose(); skyMat.dispose()
      starGeo.dispose(); starMat.dispose()
      foamGeo.dispose(); foamMat.dispose()
      mount.removeChild(renderer.domElement)
    }
  }, [])

  return (
    <div
      ref={mountRef}
      style={{ position: 'fixed', inset: 0, zIndex: 0, pointerEvents: 'none' }}
    />
  )
}
