/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using UnityEngine;

namespace Oculus.Interaction.Throw
{
    /// <summary>
    /// A helper class that uses and underlying RandomSampleConsensus to select the best pair of linear
    /// and angular velocities from a buffer of recent timed poses.
    /// </summary>
    public class RANSACVelocity
    {
        private bool _highConfidenceStreak = false;
        private float _lastProcessedTime = 0;

        private float _maxSyntheticSpeed = 5.0f;
        /// <summary>
        /// Maximum speed (in m/s) allowed for synthetic poses
        /// </summary>
        public float MaxSyntheticSpeed
        {
            get => _maxSyntheticSpeed;
            set => _maxSyntheticSpeed = Mathf.Max(_minSyntheticSpeed, value);
        }

        private const float _minSyntheticSpeed = 0.0001f;

        private RandomSampleConsensus<Vector3> _ransac;
        private RingBuffer<TimedPose> _poses;

        [Obsolete("The minHighConfidenceSamples parameter will be ignored. Use the constructor without it")]
        public RANSACVelocity(int samplesCount = 10, int samplesDeadZone = 2, int minHighConfidenceSamples = 2)
            : this(samplesCount, samplesDeadZone)
        {
        }

        public RANSACVelocity(int samplesCount = 10, int samplesDeadZone = 2)
        {
            _poses = new RingBuffer<TimedPose>(samplesCount);
            _ransac = new RandomSampleConsensus<Vector3>(samplesCount, samplesDeadZone);
        }

        public void Initialize()
        {
            _poses.Clear();
            _highConfidenceStreak = false;
        }

        public void Process(Pose pose, float time,
            bool isHighConfidence = true)
        {
            if (_poses.Count > 0 && _poses.Peek().time == time)
            {
                return;
            }

            if (!isHighConfidence)
            {
                _highConfidenceStreak = false;
            }
            else
            {
                //first high-confidence frame
                if (!_highConfidenceStreak &&
                    _poses.Count > 0)
                {
                    TimedPose repeatedPose = _poses.Peek();
                    //remove the dirty data from the buffer
                    _poses.Clear();
                    //add a first synthetic pose as if it where from the previous frame
                    //cap the speed so it is within the allowed limit
                    float distance = Vector3.Distance(pose.position, repeatedPose.pose.position);
                    float deltaTime = time - _lastProcessedTime;
                    if (Mathf.Approximately(deltaTime, 0f)
                        || distance / deltaTime > _maxSyntheticSpeed)
                    {
                        repeatedPose.time = time - (distance / _maxSyntheticSpeed);
                    }
                    else
                    {
                        repeatedPose.time = _lastProcessedTime;
                    }
                    _poses.Add(repeatedPose);
                }

                _highConfidenceStreak = true;

                TimedPose timedPose = new TimedPose(time, pose);
                _poses.Add(timedPose);
            }

            _lastProcessedTime = time;
        }

        public void GetVelocities(out Vector3 velocity, out Vector3 torque)
        {
            if (_poses.Count >= 2)
            {
                velocity = _ransac.FindOptimalModel(
                    CalculateVelocityFromSamples, ScoreDistance, _poses.Count);
                torque = _ransac.FindOptimalModel(
                    CalculateTorqueFromSamples, ScoreAngularDistance, _poses.Count);
            }
            else
            {
                velocity = Vector3.zero;
                torque = Vector3.zero;
            }
        }

        private Vector3 CalculateVelocityFromSamples(int idx1, int idx2)
        {
            GetSortedTimePoses(idx1, idx2, out TimedPose older, out TimedPose younger);
            float timeShift = younger.time - older.time;
            Vector3 positionShift = PositionOffset(younger.pose, older.pose);
            return positionShift / timeShift;
        }

        private Vector3 CalculateTorqueFromSamples(int idx1, int idx2)
        {
            GetSortedTimePoses(idx1, idx2, out TimedPose older, out TimedPose younger);
            Vector3 torque = GetTorque(older, younger);
            return torque;
        }

        protected virtual Vector3 PositionOffset(Pose youngerPose, Pose olderPose)
        {
            return youngerPose.position - olderPose.position;
        }

        private float ScoreDistance(Vector3 distance, Vector3[,] distances)
        {
            float score = 0f;
            for (int i = 0; i < _poses.Count; ++i)
            {
                for (int j = i + 1; j < _poses.Count; ++j)
                {
                    score += (distance - distances[i, j]).sqrMagnitude;
                }
            }
            return score;
        }

        protected void GetSortedTimePoses(int idx1, int idx2,
            out TimedPose older, out TimedPose younger)
        {
            int youngerIdx = idx1;
            int olderIdx = idx2;
            if (idx2 > idx1)
            {
                youngerIdx = idx2;
                olderIdx = idx1;
            }

            older = _poses[olderIdx];
            younger = _poses[youngerIdx];
        }

        private float ScoreAngularDistance(Vector3 angularDistance, Vector3[,] angularDistances)
        {
            float score = 0f;

            Quaternion target = Quaternion.Euler(angularDistance);

            for (int i = 0; i < _poses.Count; ++i)
            {
                for (int j = i + 1; j < _poses.Count; ++j)
                {
                    Quaternion sample = Quaternion.Euler(angularDistances[i, j]);
                    score += Mathf.Abs(Quaternion.Dot(target, sample));
                }
            }
            return score;
        }

        protected static Vector3 GetTorque(TimedPose older, TimedPose younger)
        {
            float timeShift = younger.time - older.time;
            Quaternion olderRot = older.pose.rotation;
            Quaternion youngerRot = younger.pose.rotation;

            //Quaternions have two ways of expressing the same rotation.
            //This code ensures that the result is the same rotation but expressed in the desired sign.
            if (Quaternion.Dot(olderRot, youngerRot) < 0)
            {
                youngerRot.x = -youngerRot.x;
                youngerRot.y = -youngerRot.y;
                youngerRot.z = -youngerRot.z;
                youngerRot.w = -youngerRot.w;
            }
            Quaternion deltaRotation = youngerRot * Quaternion.Inverse(olderRot);
            deltaRotation.ToAngleAxis(out float angularSpeed, out Vector3 torqueAxis);
            angularSpeed = (angularSpeed * Mathf.Deg2Rad) / timeShift;
            return torqueAxis * angularSpeed;
        }

        protected struct TimedPose
        {
            public float time;
            public Pose pose;

            public TimedPose(float time, Pose pose)
            {
                this.time = time;
                this.pose = pose;
            }
        }
    }
}
